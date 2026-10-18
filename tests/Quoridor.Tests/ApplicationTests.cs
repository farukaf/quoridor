using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quoridor.Services;

namespace Quoridor.Tests;

public sealed class ApplicationTests
{
    [Theory]
    [InlineData("SQLite")]
    [InlineData("PostgreSql")]
    public async Task RealApplicationProtectsIdentityAndPersistsResultsAndKeysAcrossRestart(string provider)
    {
        using var database = new TestDatabase(provider);
        try
        {
            string cookie; Guid aliceId; Guid matchId;
            using(var host=new Host(database))
            using(var client=host.CreateClient(new() { BaseAddress=new Uri("https://localhost"),HandleCookies=false }))
            {
                var response=await client.GetAsync("/"); response.EnsureSuccessStatusCode();
                var header=Assert.Single(response.Headers.GetValues("Set-Cookie"),x=>x.StartsWith("Quoridor.Player="));
                Assert.Contains("httponly",header); Assert.Contains("secure",header); Assert.Contains("samesite=lax",header);
                cookie=header.Split(';')[0];
                var options=host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
                var ticket=options.TicketDataFormat.Unprotect(cookie.Split('=',2)[1]); Assert.NotNull(ticket);
                aliceId=Guid.Parse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var store=host.Services.GetRequiredService<GameStore>(); var alice=store.SetName(aliceId,"<script>alert(1)</script>"); var bob=store.SetName(Guid.NewGuid(),"Bob"); matchId=Guid.NewGuid();
                store.Complete(new(matchId,Guid.NewGuid(),alice.Id,alice.UserName,bob.Id,bob.UserName,new(2026,10,17,12,0,0,TimeSpan.Zero)));
                client.DefaultRequestHeaders.Add("Cookie",cookie);
                var home=await client.GetStringAsync("/"); Assert.Contains("data-wins",home); Assert.Contains("&lt;script&gt;",home); Assert.DoesNotContain("<script>alert(1)</script>",home);
                var history=await client.GetStringAsync($"/Players/{aliceId}"); Assert.Contains(matchId.ToString(),history); Assert.Contains("Win",history);
                (await client.GetAsync($"/Room/{Guid.NewGuid()}")).EnsureSuccessStatusCode();
                (await client.PostAsync("/_blazor/negotiate?negotiateVersion=1",new StringContent(""))).EnsureSuccessStatusCode();
            }
            using(var restarted=new Host(database))
            using(var client=restarted.CreateClient(new() { BaseAddress=new Uri("https://localhost"),HandleCookies=false }))
            {
                client.DefaultRequestHeaders.Add("Cookie",cookie);
                var response=await client.GetAsync($"/Players/{aliceId}"); response.EnsureSuccessStatusCode(); Assert.Contains(matchId.ToString(),await response.Content.ReadAsStringAsync());
                Assert.False(response.Headers.TryGetValues("Set-Cookie",out var headers) && headers.Any(x=>x.StartsWith("Quoridor.Player=")));
                client.DefaultRequestHeaders.Remove("Cookie"); client.DefaultRequestHeaders.Add("Cookie","Quoridor.Player=forged-client-guid");
                var forged=await client.GetAsync("/"); Assert.True(forged.Headers.TryGetValues("Set-Cookie",out var issued)); Assert.Contains(issued!,x=>x.StartsWith("Quoridor.Player="));
            }
        }
        finally { /* Hosts dispose their pools before the isolated database is removed. */ }
    }
    private sealed class Host(TestDatabase database) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(database.Values));
        }
    }
}
