using LimitIO.Core.Models;
using LimitIO.Core.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LimitIO.Core.Tests;

[TestClass]
public class ConfigStoreTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LimitIOTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Load_NoFileYet_ReturnsFreshConfig()
    {
        var store = new ConfigStore(Path.Combine(_tempDir, "config.json"));

        var config = store.Load();

        Assert.IsFalse(config.PasswordConfigured);
        Assert.AreEqual(0, config.Rules.Count);
    }

    [TestMethod]
    public void SaveThenLoad_RoundTripsRulesAndPassword()
    {
        var path = Path.Combine(_tempDir, "config.json");
        var store = new ConfigStore(path);

        var config = new AppConfig
        {
            PasswordHash = [1, 2, 3, 4],
            PasswordSalt = [5, 6, 7, 8],
            PasswordIterations = 310_000,
        };
        config.Rules.Add(LimitRule.Create(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30)));
        config.Rules.Add(LimitRule.Create(LimitTargetType.ProcessName, "telegram.exe", TimeSpan.FromMinutes(45)));

        store.Save(config);

        var reloaded = new ConfigStore(path).Load();

        Assert.IsTrue(reloaded.PasswordConfigured);
        CollectionAssert.AreEqual(config.PasswordHash, reloaded.PasswordHash);
        CollectionAssert.AreEqual(config.PasswordSalt, reloaded.PasswordSalt);
        Assert.AreEqual(2, reloaded.Rules.Count);
        Assert.AreEqual("instagram.com", reloaded.Rules[0].Target);
        Assert.AreEqual(TimeSpan.FromMinutes(45), reloaded.Rules[1].DailyBudget);
    }

    [TestMethod]
    public void Save_OverwritingExistingFile_DoesNotLeaveTempFileBehind()
    {
        var path = Path.Combine(_tempDir, "config.json");
        var store = new ConfigStore(path);

        store.Save(new AppConfig());
        store.Save(new AppConfig { PasswordIterations = 42 });

        Assert.IsFalse(File.Exists(path + ".tmp"));
        Assert.AreEqual(42, store.Load().PasswordIterations);
    }
}
