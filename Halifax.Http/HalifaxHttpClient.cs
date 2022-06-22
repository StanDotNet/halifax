using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Halifax.Core.Helpers;
using Halifax.Domain;
using Halifax.Domain.Exceptions;

namespace Halifax.Http;

public abstract class HalifaxHttpClient
{
    protected readonly HttpClient http;
    
    private static readonly List<HttpStatusCode> exceptionHttpStatuses = new()
    {
        HttpStatusCode.NotFound,
        HttpStatusCode.BadRequest,
        HttpStatusCode.Unauthorized
    };

    protected HalifaxHttpClient(HttpClient http)
    {
        this.http = http;
    }

    protected virtual HttpRequestMessage CreateMessage(HttpMethod method, string url, object body = default)
    {
        var message = new HttpRequestMessage(method, url);

        if (body != null)
        {
            var json = Json.Serialize(body);
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return message;
    }

    protected virtual Task<TModel> SendAsync<TModel>(
        HttpRequestMessage message,
        CancellationToken cancellationToken = default) where TModel : class
    {
        var opts = new JsonSerializerOptions();
        Json.ConfigureOptions(opts);
        return SendAsync<TModel>(message, opts, cancellationToken);
    }
    
    protected virtual async Task<TModel> SendAsync<TModel>(
        HttpRequestMessage message, 
        JsonSerializerOptions jsonSerializerOptions,
        CancellationToken cancellationToken = default) where TModel : class
    {
        using var response = await http.SendAsync(message, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            try
            {
                var model = await response.Content.ReadFromJsonAsync<ApiResponse<TModel>>(
                    jsonSerializerOptions,
                    cancellationToken);
                
                return model?.Data;
            }
            catch (Exception ex)
            {
                L.Error($"Error parsing the response. {message.RequestUri}", ex);
                throw ErrorReadingTheResponse();
            }
        }

        await HandleUnsuccessfulResponseAsync(response);
        
        // exception is thrown in most cases
        return null;
    }
    
    protected virtual async Task HandleUnsuccessfulResponseAsync(HttpResponseMessage response)
    {
        var code = response.StatusCode;
        
        if (exceptionHttpStatuses.Contains(code))
        {
            ApiResponse model;
            
            try
            {
                model = await response.Content.ReadFromJsonAsync<ApiResponse>();
                
                if (string.IsNullOrWhiteSpace(model?.Error?.Message))
                {
                    L.Warning("Response model doesn't have error information");
                    throw ErrorReadingTheResponse();
                }
            }
            catch (Exception ex)
            {
                L.Error($"Error parsing the response. {response.RequestMessage?.RequestUri}", ex);
                throw ErrorReadingTheResponse();
            }

            
            switch (code)
            {
                case HttpStatusCode.BadRequest:
                    throw new HalifaxException(model.Error.Message);
                
                case HttpStatusCode.NotFound:
                    throw new HalifaxNotFoundException(model.Error.Message);
                
                case HttpStatusCode.Unauthorized:
                    throw new HalifaxUnauthorizedException(model.Error.Message);
            }
        }
        
        throw new Exception($"Unsuccessful request. {GetType().Name}. HTTP {code}");
    }

    private Exception ErrorReadingTheResponse()
    {
        return new Exception($"Error reading the response of the {GetType().Name}");
    }
}