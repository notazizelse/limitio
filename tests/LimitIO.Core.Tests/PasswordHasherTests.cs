using LimitIO.Core.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LimitIO.Core.Tests;

[TestClass]
public class PasswordHasherTests
{
    [TestMethod]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var result = PasswordHasher.Hash("correct horse battery staple", iterations: 1000);

        Assert.IsTrue(PasswordHasher.Verify("correct horse battery staple", result.Hash, result.Salt, result.Iterations));
    }

    [TestMethod]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var result = PasswordHasher.Hash("correct horse battery staple", iterations: 1000);

        Assert.IsFalse(PasswordHasher.Verify("wrong password", result.Hash, result.Salt, result.Iterations));
    }

    [TestMethod]
    public void Hash_SamePasswordTwice_ProducesDifferentSaltsAndHashes()
    {
        var first = PasswordHasher.Hash("same password", iterations: 1000);
        var second = PasswordHasher.Hash("same password", iterations: 1000);

        CollectionAssert.AreNotEqual(first.Salt, second.Salt);
        CollectionAssert.AreNotEqual(first.Hash, second.Hash);
    }

    [TestMethod]
    public void Verify_EmptyPassword_ReturnsFalse()
    {
        var result = PasswordHasher.Hash("real password", iterations: 1000);

        Assert.IsFalse(PasswordHasher.Verify("", result.Hash, result.Salt, result.Iterations));
    }

    [TestMethod]
    public void Hash_EmptyPassword_Throws()
    {
        Assert.ThrowsException<ArgumentException>(() => PasswordHasher.Hash(""));
    }
}
