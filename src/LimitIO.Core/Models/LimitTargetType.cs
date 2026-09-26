namespace LimitIO.Core.Models;

/// <summary>What a <see cref="LimitRule"/> matches network activity against.</summary>
public enum LimitTargetType
{
    /// <summary>A website hostname (e.g. "instagram.com"), matched by suffix so subdomains count too.</summary>
    Domain = 0,

    /// <summary>An executable name (e.g. "telegram.exe"), matched case-insensitively, ".exe" optional.</summary>
    ProcessName = 1,
}
