using LimitIO.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LimitIO.Core.Tests;

[TestClass]
public class LimitRuleTests
{
    [TestMethod]
    public void DomainRule_MatchesExactHost()
    {
        var rule = LimitRule.Create(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));

        Assert.IsTrue(rule.Matches("instagram.com", null));
    }

    [TestMethod]
    public void DomainRule_MatchesSubdomain()
    {
        var rule = LimitRule.Create(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));

        Assert.IsTrue(rule.Matches("www.instagram.com", null));
        Assert.IsTrue(rule.Matches("graph.instagram.com", null));
    }

    [TestMethod]
    public void DomainRule_DoesNotMatchUnrelatedDomainWithSameSuffix()
    {
        var rule = LimitRule.Create(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));

        // "notinstagram.com" ends with "instagram.com" as a raw string but is not a subdomain of it.
        Assert.IsFalse(rule.Matches("notinstagram.com", null));
    }

    [TestMethod]
    public void DomainRule_IsCaseInsensitive()
    {
        var rule = LimitRule.Create(LimitTargetType.Domain, "Instagram.COM", TimeSpan.FromMinutes(30));

        Assert.IsTrue(rule.Matches("INSTAGRAM.com", null));
    }

    [TestMethod]
    public void ProcessRule_MatchesWithOrWithoutExeSuffix()
    {
        var rule = LimitRule.Create(LimitTargetType.ProcessName, "telegram.exe", TimeSpan.FromMinutes(45));

        Assert.IsTrue(rule.Matches(null, "telegram.exe"));
        Assert.IsTrue(rule.Matches(null, "Telegram"));
        Assert.IsTrue(rule.Matches(null, "TELEGRAM.EXE"));
    }

    [TestMethod]
    public void ProcessRule_DoesNotMatchDifferentProcess()
    {
        var rule = LimitRule.Create(LimitTargetType.ProcessName, "telegram.exe", TimeSpan.FromMinutes(45));

        Assert.IsFalse(rule.Matches(null, "chrome.exe"));
    }

    [TestMethod]
    public void DisabledRule_NeverMatches()
    {
        var rule = LimitRule.Create(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));
        rule.Enabled = false;

        Assert.IsFalse(rule.Matches("instagram.com", null));
    }

    [TestMethod]
    public void DomainRule_DoesNotMatchProcessNameField()
    {
        var rule = LimitRule.Create(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));

        Assert.IsFalse(rule.Matches(null, "instagram.com"));
    }
}
