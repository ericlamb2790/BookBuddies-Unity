using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BookBuddies.Net
{
    /// <summary>
    /// Requests to plain http:// servers: a friend's world on your network (Local/LocalHost) or a test server on this PC.
    /// These go through one shared HttpClient that never uses the system proxy (the project allows plain http, see
    /// Player Settings → "Allow downloads over HTTP"), and answer the way
    /// UnityWebRequest does: the status (0 when the server couldn't be reached in time), the reply's text, and what went
    /// wrong (null when nothing did). BBApi turns that into a reply or an ApiError. It carries on on the caller's thread.
    /// </summary>
    public static class PlainHttp
    {
        // straight to the address, never through a proxy: these servers are on your network or a forwarded port
        static readonly HttpClient client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };

        /// <summary>One request: the JSON body (or null) goes as application/json, the token (or null) as "Authorization: Bearer".</summary>
        public static async Task<(long status, string text, string error)> Send(string url, string method, string json, string token, int seconds)
        {
            using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(seconds)))
            using (var req = new HttpRequestMessage(new HttpMethod(method), url))
            {
                if (json != null)
                {
                    req.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
                    req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                }
                if (!string.IsNullOrEmpty(token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                try
                {
                    // the whole reply arrives inside the time allowed (SendAsync reads it all before it returns)
                    using (var res = await client.SendAsync(req, cancel.Token))
                    {
                        string text = await res.Content.ReadAsStringAsync();
                        int status = (int)res.StatusCode;
                        return (status, text, res.IsSuccessStatusCode ? null : $"HTTP/1.1 {status} {res.ReasonPhrase}");
                    }
                }
                catch (OperationCanceledException) { return (0, "", "Request timeout"); }
                catch (Exception e) { return (0, "", e.Message); } // refused, no route, cut off: it couldn't be reached
            }
        }
    }
}
