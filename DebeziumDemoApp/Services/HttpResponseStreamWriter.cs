using System.Security.Cryptography;

namespace DebeziumDemoApp.Services;

public class HttpResponseStreamWriter : IResponseStreamWriter
{
    private readonly HttpResponse _response;

    public HttpResponseStreamWriter(HttpResponse response)
    {
        _response = response;
    }

    public async Task WriteAsync(string data)
    {
        await _response.WriteAsync(data);
    }

    public async Task FlushAsync()
    {
        await _response.Body.FlushAsync();
    }
}