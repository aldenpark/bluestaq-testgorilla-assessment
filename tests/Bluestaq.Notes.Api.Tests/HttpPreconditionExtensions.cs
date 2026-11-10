using System.Net.Http.Json;

namespace Bluestaq.Notes.Api.Tests;

/// <summary>Builds integration-test mutations with an ETag read from the target resource.</summary>
internal static class HttpPreconditionExtensions
{
    public static Task<HttpResponseMessage> PostTeamAsync<T>(
        this HttpClient client,
        Guid teamId,
        string suffix,
        T value) =>
        client.SendWithCurrentEtagAsync(
            HttpMethod.Post,
            $"/api/teams/{teamId}{suffix}",
            $"/api/teams/{teamId}",
            JsonContent.Create(value));

    public static Task<HttpResponseMessage> PatchTeamAsync<T>(this HttpClient client, Guid teamId, T value) =>
        client.SendWithCurrentEtagAsync(
            HttpMethod.Patch,
            $"/api/teams/{teamId}",
            $"/api/teams/{teamId}",
            JsonContent.Create(value));

    public static Task<HttpResponseMessage> PutTeamAsync<T>(
        this HttpClient client,
        Guid teamId,
        string suffix,
        T value) =>
        client.SendWithCurrentEtagAsync(
            HttpMethod.Put,
            $"/api/teams/{teamId}{suffix}",
            $"/api/teams/{teamId}",
            JsonContent.Create(value));

    public static Task<HttpResponseMessage> DeleteTeamAsync(this HttpClient client, Guid teamId, string suffix = "") =>
        client.SendWithCurrentEtagAsync(
            HttpMethod.Delete,
            $"/api/teams/{teamId}{suffix}",
            $"/api/teams/{teamId}");

    public static Task<HttpResponseMessage> PutNoteAsync<T>(
        this HttpClient client,
        Guid noteId,
        string suffix,
        T value) =>
        client.SendWithCurrentEtagAsync(
            HttpMethod.Put,
            $"/api/notes/{noteId}{suffix}",
            $"/api/notes/{noteId}",
            JsonContent.Create(value));

    public static Task<HttpResponseMessage> DeleteNoteAsync(this HttpClient client, Guid noteId, string suffix = "") =>
        client.SendWithCurrentEtagAsync(
            HttpMethod.Delete,
            $"/api/notes/{noteId}{suffix}",
            $"/api/notes/{noteId}");

    private static async Task<HttpResponseMessage> SendWithCurrentEtagAsync(
        this HttpClient client,
        HttpMethod method,
        string requestUri,
        string resourceUri,
        HttpContent? content = null)
    {
        using var current = await client.GetAsync(resourceUri);
        current.EnsureSuccessStatusCode();
        var etag = current.Headers.ETag?.Tag
            ?? throw new InvalidOperationException($"{resourceUri} did not return an ETag.");
        using var request = new HttpRequestMessage(method, requestUri) { Content = content };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request);
    }
}
