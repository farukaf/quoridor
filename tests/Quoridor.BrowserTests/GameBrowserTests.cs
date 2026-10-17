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
        if (Environment.GetEnvironmentVariable("CHROME_BINARY") is { Length: > 0 } binary) options.BinaryLocation = binary;
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

    [Fact]
    public void TwoPlayersCanJoinMoveAndPlaceWall()
    {
        using var first = Browser();
        using var second = Browser();
        try
        {
            var baseUrl = Environment.GetEnvironmentVariable("APP_BASE_URL") ?? "http://127.0.0.1:8080";
            first.Navigate().GoToUrl(baseUrl);
            Assert.Contains("Welcome", first.PageSource);
            var room = $"{baseUrl.TrimEnd('/')}/Room/{Guid.NewGuid()}";
            Join(first, room, "Selenium Alice");
            Join(second, room, "Selenium Bob");
            Wait(first).Until(d => d.PageSource.Contains("Selenium Bob"));
            Click(first, By.XPath("//div[contains(@class,'cell')][small[normalize-space()='(r0,c4)']]"));
            Wait(first).Until(d => d.FindElements(By.CssSelector(".avaliable")).Count > 0);
            Click(first, By.XPath("//div[contains(@class,'cell')][small[normalize-space()='(r1,c4)']]"));
            Wait(second).Until(d => d.FindElements(By.XPath("//li[small[contains(.,'Player 2')]][small[normalize-space()='TURN']]")).Count == 1);
            Click(second, By.CssSelector("button.enabled"));
            Click(second, By.CssSelector("button.avaliable"));
            Wait(second).Until(d => d.FindElements(By.XPath("//li[small[contains(.,'Player 2')]]//li[contains(.,'Walls: 9')]")).Count == 1);
            Wait(first).Until(d => d.FindElements(By.XPath("//li[small[contains(.,'Player 1')]][small[normalize-space()='TURN']]")).Count == 1);
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
