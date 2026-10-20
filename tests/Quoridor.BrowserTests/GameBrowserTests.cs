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

    private static WebDriverWait Wait(IWebDriver browser)
    {
        var wait = new WebDriverWait(browser,TimeSpan.FromSeconds(30));
        wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException));
        return wait;
    }
    private static void Click(IWebDriver browser, By selector)
    {
        var element = Wait(browser).Until(d => d.FindElements(selector).FirstOrDefault(e => e.Displayed && e.Enabled));
        ((IJavaScriptExecutor)browser).ExecuteScript("arguments[0].scrollIntoView({block:'center',behavior:'instant'});", element);
        element.Click();
    }
    private static void Join(IWebDriver browser, string url, string name, bool navigate = true)
    {
        if (navigate) browser.Navigate().GoToUrl(url);
        Wait(browser).Until(d => d.FindElements(By.Id("player-name")).FirstOrDefault(e => e.Displayed))!.SendKeys(name);
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

    private static void NoOverflow(IWebDriver browser)
    {
        Assert.True(((IJavaScriptExecutor)browser).ExecuteScript("return document.documentElement.scrollWidth <= window.innerWidth + 1") is true, "Page must fit the viewport without horizontal scrolling.");
    }
    private static void Screenshot(IWebDriver browser, string name, int width)
    {
        NoOverflow(browser);
        var directory = Environment.GetEnvironmentVariable("BROWSER_ARTIFACTS") ?? "artifacts/browser";
        Directory.CreateDirectory(directory);
        ((ITakesScreenshot)browser).GetScreenshot().SaveAsFile(Path.Combine(directory,$"{name}-{width}.png"));
    }

    [Theory]
    [InlineData(390)]
    [InlineData(768)]
    [InlineData(1440)]
    public void TwoPlayersCanCompleteGameAndSeeExactlyOneWinAndHistory(int width)
    {
        using var first = Browser();
        using var second = Browser();
        try
        {
            first.Manage().Window.Size = new System.Drawing.Size(width, 1100);
            second.Manage().Window.Size = new System.Drawing.Size(width, 1100);
            var baseUrl = Environment.GetEnvironmentVariable("APP_BASE_URL") ?? "http://127.0.0.1:8080";
            first.Navigate().GoToUrl(baseUrl);
            Assert.Contains("clever detour", first.PageSource);
            Screenshot(first,"home",width);
            first.FindElement(By.TagName("body")).SendKeys(Keys.Tab);
            Assert.Contains(first.SwitchTo().ActiveElement().TagName,new[]{"a","button","input"});
            Click(first,By.CssSelector(".hero-actions .button"));
            Wait(first).Until(d=>d.Url.Contains("/Room/") && Guid.TryParse(new Uri(d.Url).Segments.Last(),out _));
            var room = first.Url;
            var alice = "Alice " + Guid.NewGuid().ToString("N")[..8] + " RemarkablyThoughtful";
            var bob = "Bob " + Guid.NewGuid().ToString("N")[..8] + " A Very Clever Opponent";
            Join(first, room, alice, navigate:false);
            Join(second, room, bob);
            Wait(first).Until(d => d.PageSource.Contains(bob));
            Click(first,By.CssSelector("button[aria-label='Copy room invitation link']"));
            Wait(first).Until(d=>!string.IsNullOrWhiteSpace(d.FindElement(By.CssSelector(".share-status")).Text));
            Assert.Equal(room,first.FindElement(By.Id("room-link")).GetAttribute("value"));
            Assert.Empty(first.FindElements(By.XPath("//button[normalize-space()='Move a pawn' or normalize-space()='Place a wall']")));
            Assert.True(first.FindElement(By.CssSelector(".cell-control")).Size.Width>=24);
            Assert.True(first.FindElement(By.CssSelector(".cell-control")).Size.Height>=24);
            ((IJavaScriptExecutor)first).ExecuteScript("window.scrollTo({top:0,behavior:'instant'})");
            Screenshot(first,"room",width);
            ((IJavaScriptExecutor)first).ExecuteScript("document.querySelector('.board-stage').scrollIntoView({block:'start',behavior:'instant'});");
            Screenshot(first,"game",width);
            Assert.True(((IJavaScriptExecutor)first).ExecuteScript("return [...document.querySelectorAll('.turn-pill,.you-pill')].every(element => parseFloat(getComputedStyle(element).fontSize) >= 10)") is true);
            Assert.True(((IJavaScriptExecutor)first).ExecuteScript("return [...document.querySelectorAll('button')].every(button => (button.getAttribute('aria-label') || button.innerText).trim().length > 0)") is true);
            var rules = first.FindElement(By.CssSelector(".rules-details summary"));
            rules.SendKeys(Keys.Enter);
            Wait(first).Until(d=>d.FindElement(By.CssSelector(".rules-details")).GetAttribute("open") is not null);
            rules.SendKeys(Keys.Enter);
            // Exercise native keyboard controls: select pawn, arrow to a legal square, select.
            var pawn = first.FindElement(Cell(0,4)).FindElement(By.CssSelector("button"));
            Assert.False(string.IsNullOrWhiteSpace(pawn.GetAttribute("aria-label")));
            pawn.SendKeys(Keys.Enter);
            Wait(first).Until(d => d.FindElements(By.CssSelector(".avaliable")).Count > 0);
            first.SwitchTo().ActiveElement().SendKeys(Keys.ArrowDown);
            Assert.Contains("row 2, column 5",first.SwitchTo().ActiveElement().GetAttribute("aria-label"));
            Assert.True(((IJavaScriptExecutor)first).ExecuteScript("return getComputedStyle(document.activeElement).outlineStyle !== 'none'") is true);
            first.SwitchTo().ActiveElement().SendKeys(Keys.Enter);
            Wait(second).Until(d => d.FindElements(By.XPath("//li[small[contains(.,'Player 2')]][small[normalize-space()='TURN']]")).Count == 1);
            Wait(second).Until(d=>d.FindElement(By.CssSelector("button[data-corner='1,0']")).Enabled);
            Assert.True(second.FindElement(By.CssSelector("button[data-corner='1,0']")).Size.Width >= 24);
            Assert.True(second.FindElement(By.CssSelector("button[data-corner='1,0']")).Size.Height >= 24);
            Assert.Null(second.FindElement(Cell(8,4)).FindElement(By.CssSelector(".cell-control")).GetAttribute("disabled"));
            Click(second, By.CssSelector("button[data-corner='1,0']"));
            Wait(second).Until(d=>d.FindElement(By.CssSelector("button[data-corner='1,2']")).GetAttribute("class")?.Contains("avaliable") == true);
            var secondWallPoint = second.FindElement(By.CssSelector("button[data-corner='1,2']"));
            ((IJavaScriptExecutor)second).ExecuteScript("arguments[0].focus();", secondWallPoint);
            secondWallPoint.SendKeys(Keys.Space);
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
            Assert.False(string.IsNullOrWhiteSpace(first.FindElement(By.CssSelector(".turn-banner [role=status]")).Text));
            var identityCookie = first.Manage().Cookies.GetCookieNamed("Quoridor.Player");
            Assert.NotNull(identityCookie);
            Assert.True(identityCookie.IsHttpOnly);
            first.Navigate().GoToUrl(baseUrl);
            var winner = Wait(first).Until(d => d.FindElements(By.CssSelector("tbody tr")).FirstOrDefault(e => e.Text.Contains(alice)));
            Assert.Equal("1", winner!.FindElement(By.CssSelector("[data-wins]")).Text);
            ((IJavaScriptExecutor)first).ExecuteScript("document.querySelector('#leaderboard').scrollIntoView({block:'start',behavior:'instant'});");
            Screenshot(first,"leaderboard",width);
            var aliceId = winner.GetAttribute("data-player");
            ((IJavaScriptExecutor)second).ExecuteScript("localStorage.setItem('USER_KEY', JSON.stringify({ id: arguments[0], userName: 'Forged Alice', timezone: 'UTC' }));", aliceId);
            second.Navigate().GoToUrl(room);
            Wait(second).Until(d => d.FindElements(By.XPath("//li[small[contains(.,'Player 2')]]//small[normalize-space()='YOU']")).Count == 1);
            Assert.DoesNotContain("Forged Alice", second.FindElement(By.CssSelector("body")).Text);
            Click(first, By.CssSelector($"tr[data-player='{aliceId}'] a"));
            Wait(first).Until(d => d.FindElements(By.CssSelector("tr[data-match]")).Count == 1);
            Assert.Contains("Win", first.PageSource);
            Assert.Contains(bob, first.PageSource);
            ((IJavaScriptExecutor)first).ExecuteScript("window.scrollTo({top:0,behavior:'instant'})");
            Screenshot(first,"history",width);
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
