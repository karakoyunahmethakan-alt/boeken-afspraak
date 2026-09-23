using System.Net;
using System.Text;

namespace BoekenAfspraak.Tests;

// Stands in for the real Brevo transactional-email HTTP API during tests, so
// no test run ever makes a real outbound network call or sends a real email.
// Always returns a successful-looking response, mirroring Brevo's actual
// 201 response shape ({"messageId": "..."}).
public class FakeBrevoHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{\"messageId\":\"fake-message-id\"}", Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
