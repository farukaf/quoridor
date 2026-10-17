using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Remote;
using OpenQA.Selenium.Support.UI;

namespace Quoridor.BrowserTests;

public class GameBrowserTests
{
    private static IWebDriver Browser()
    {
        var options = new ChromeOptions();
        options.AddArguments("--headless=new", "--no-sandbox", "--disable-dev-shm-usage", "--window-size=1440,1200");
        if (Environment.GetEnvironmentVariable("CHROME_BINARY") is { Length: > 0 } binary)
            options.BinaryLocation = binary;
        options.SetLoggingPreference(LogType.Browser, LogLevel.All);
        var remote = Environment.GetEnvironmentVariable("SELENIUM_URL");
        return string.IsNullOrWhiteSpace(remote) ? new ChromeDriver(options) : new RemoteWebDriver(new Uri(remote), options);
    }

    private static WebDriverWait Wait(IWebDriver browser) => new(browser, TimeSpan.FromSeconds(30));
    private static void Click(IWebDriver browser, By selector)
    {
        var element = Wait(browser).Until(d => d.FindElements(selector).FirstOrDefault(e => e.Displayed && e.Enabled));
        ((IJavaScriptExecutor)browser).ExecuteScript("arguments[0].scrollIntoView({block:'center'});", element);
        element.Click();
    }
    private static void Join(IWebDriver browser, string url, string name)
    {
        browser.Navigate().GoToUrl(url);
        Wait(browser).Until(d => d.FindElements(By.CssSelector("input")).FirstOrDefault(e => e.Displayed))!.SendKeys(name);
        Click(browser, By.CssSelector("button[type=submit]"));
        Wait(browser).Until(d => d.FindElements(By.CssSelector(".cell.text-center")).Count == 81);
    }

    private static By Cell(int row, int column) => By.XPath($"//div[contains(@class,'cell')][small[normalize-space()='(r{row},c{column})']]");
    private static void Move(IWebDriver driver, int fromRow, int fromColumn, int row, int column)
    {
        Click(driver, Cell(fromRow, fromColumn));
        Wait(driver).Until(d => d.FindElements(By.CssSelector(".avaliable")).Count > 0);
        Click(driver, Cell(row, column));
    }

    private static bool IsReady(string baseUrl)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            return client.GetAsync(baseUrl).GetAwaiter().GetResult().IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    [Fact]
    public void TwoPlayersCanCompleteGameAndSeeExactlyOneWinAndHistory()
    {
        using var first = Browser();
        using var second = Browser();
        try
        {
            var baseUrl = Environment.GetEnvironmentVariable("APP_BASE_URL") ?? "http://127.0.0.1:8080";
            first.Navigate().GoToUrl(baseUrl);
            Assert.Contains("Welcome", first.PageSource);
            var room = $"{baseUrl.TrimEnd('/')}/Room/{Guid.NewGuid()}";
            var alice = "Alice " + Guid.NewGuid().ToString("N")[..8];
            var bob = "Bob " + Guid.NewGuid().ToString("N")[..8];
            Join(first, room, alice);
            Join(second, room, bob);
            Wait(first).Until(d => d.PageSource.Contains(bob));
            Click(first, By.XPath("//div[contains(@class,'cell')][small[normalize-space()='(r0,c4)']]"));
            Wait(first).Until(d => d.FindElements(By.CssSelector(".avaliable")).Count > 0);
            Click(first, By.XPath("//div[contains(@class,'cell')][small[normalize-space()='(r1,c4)']]"));
            Wait(second).Until(d => d.FindElements(By.XPath("//li[small[contains(.,'Player 2')]][small[normalize-space()='TURN']]")).Count == 1);
            Click(second, By.CssSelector("button[data-corner='1,0']"));
            Click(second, By.CssSelector("button[data-corner='1,2'].avaliable"));
            Wait(second).Until(d => d.FindElements(By.XPath("//li[small[contains(.,'Player 2')]]//li[contains(.,'Walls: 9')]")).Count == 1);
            Wait(first).Until(d => d.FindElements(By.XPath("//li[small[contains(.,'Player 1')]][small[normalize-space()='TURN']]")).Count == 1);
            // Finish by legal alternating moves; Bob steps aside and then shuffles.
            for (var row = 2; row <= 8; row++)
            {
                Move(first, row - 1, 4, row, 4);
                if (row < 8)
                {
                    Wait(second).Until(d => d.FindElements(By.XPath("//li[small[contains(.,'Player 2')]][small[normalize-space()='TURN']]")).Count == 1);
                    var from = row == 2 ? 4 : (row % 2 == 1 ? 5 : 6);
                    var to = row % 2 == 0 ? 5 : 6;
                    Move(second, 8, from, 8, to);
                    Wait(first).Until(d => d.FindElements(By.XPath("//li[small[contains(.,'Player 1')]][small[normalize-space()='TURN']]")).Count == 1);
                }
            }
            Wait(first).Until(d => d.FindElements(By.XPath("//li[small[contains(.,'Player 1')]][small[normalize-space()='TURN']]")).Count == 1);
            // A repeated click on the previous finish square cannot create another result.
            Click(first, Cell(8, 4));
            var identityCookie = first.Manage().Cookies.GetCookieNamed("Quoridor.Player");
            Assert.NotNull(identityCookie);
            Assert.True(identityCookie.IsHttpOnly);
            first.Navigate().GoToUrl(baseUrl);
            var winner = Wait(first).Until(d => d.FindElements(By.CssSelector("tbody tr")).FirstOrDefault(e => e.Text.Contains(alice)));
            Assert.Equal("1", winner!.FindElement(By.CssSelector("[data-wins]")).Text);
            var aliceId = winner.GetAttribute("data-player");
            ((IJavaScriptExecutor)second).ExecuteScript("localStorage.setItem('USER_KEY', JSON.stringify({ id: arguments[0], userName: 'Forged Alice', timezone: 'UTC' }));", aliceId);
            second.Navigate().GoToUrl(room);
            Wait(second).Until(d => d.FindElements(By.XPath("//li[small[contains(.,'Player 2')]]//small[normalize-space()='YOU']")).Count == 1);
            Assert.DoesNotContain("Forged Alice", second.FindElement(By.CssSelector("body")).Text);
            winner.FindElement(By.TagName("a")).Click();
            Wait(first).Until(d => d.FindElements(By.CssSelector("tr[data-match]")).Count == 1);
            Assert.Contains("Win", first.PageSource);
            Assert.Contains(bob, first.PageSource);
            if (Environment.GetEnvironmentVariable("APP_RESTART_CONTAINER") is { Length: > 0 } container)
            {
                var command = new System.Diagnostics.ProcessStartInfo("docker") { RedirectStandardError = true, RedirectStandardOutput = true };
                command.ArgumentList.Add("restart");
                command.ArgumentList.Add(container);
                using var process = System.Diagnostics.Process.Start(command)!;
                Assert.True(process.WaitForExit(30000));
                Assert.Equal(0, process.ExitCode);
                Wait(first).Until(_ => IsReady(baseUrl));
            }
            first.Navigate().Refresh();
            Wait(first).Until(d => d.FindElements(By.CssSelector("tr[data-match]")).Count == 1);
            Assert.Equal(identityCookie.Value, first.Manage().Cookies.GetCookieNamed("Quoridor.Player")!.Value);
        }
        catch
        {
            var directory = Environment.GetEnvironmentVariable("BROWSER_ARTIFACTS") ?? "artifacts/browser";
            Directory.CreateDirectory(directory);
            foreach (var (driver, name) in new[] { (first, "alice"), (second, "bob") })
            {
                ((ITakesScreenshot)driver).GetScreenshot().SaveAsFile(Path.Combine(directory, $"{name}.png"));
                File.WriteAllText(Path.Combine(directory, $"{name}.html"), driver.PageSource);
                File.WriteAllLines(Path.Combine(directory, $"{name}.log"), driver.Manage().Logs.GetLog(LogType.Browser).Select(l => l.ToString()));
            }
            throw;
        }
    }
}
