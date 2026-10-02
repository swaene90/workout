using System.Net;
using Microsoft.AspNetCore.Hosting;
using Xunit;
namespace Workout.Tests;

public class PwaStaticTests
{
    private sealed class StaticFactory : ApiFactory
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "workout-static-" + Guid.NewGuid());
        public StaticFactory()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "index.html"), "<!doctype html><title>Workout</title>");
            File.WriteAllText(Path.Combine(Root, "sw.js"), "self.addEventListener('push',()=>{});");
            File.WriteAllText(Path.Combine(Root, "manifest.webmanifest"), "{\"id\":\"/\",\"display\":\"standalone\"}");
        }
        protected override void ConfigureWebHost(IWebHostBuilder b) { base.ConfigureWebHost(b); b.UseWebRoot(Root); }
    }
    [Fact]
    public async Task ManifestAndWorkerAreServedWithCorrectTypesAndRevalidation()
    {
        await using var app = new StaticFactory(); using var client = app.Client();
        var manifest = await client.GetAsync("/manifest.webmanifest"); Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        Assert.Equal("application/manifest+json", manifest.Content.Headers.ContentType?.MediaType); Assert.True(manifest.Headers.CacheControl!.NoCache);
        var worker = await client.GetAsync("/sw.js"); Assert.Equal(HttpStatusCode.OK, worker.StatusCode);
        Assert.Contains("javascript", worker.Content.Headers.ContentType!.MediaType); Assert.True(worker.Headers.CacheControl!.NoCache);
        Assert.True((await client.GetAsync("/api/me")).Headers.CacheControl!.NoStore);
        Assert.True((await client.GetAsync("/auth/login")).Headers.CacheControl!.NoStore);
        await app.DisposeAsync();
        var full = Path.GetFullPath(app.Root); var tempRoot = Path.GetFullPath(Path.GetTempPath());
        Assert.StartsWith(tempRoot, full); Directory.Delete(full, recursive:true);
    }
}
