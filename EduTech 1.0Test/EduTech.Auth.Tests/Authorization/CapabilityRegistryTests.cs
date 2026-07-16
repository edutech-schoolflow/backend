using System.Reflection;
using EduTech.Shared.Authorization;
using EduTech.Shared.Constants;

namespace EduTech.Auth.Tests.Authorization;

/// <summary>
/// Integrity of the capability catalog (EDD-006): every declared capability constant is registered
/// exactly once, and the legacy-flag bridge is a faithful 1:1 cover of the 13 flags. These guard
/// against a capability being added to <c>Capabilities</c> but forgotten in <c>CapabilityRegistry</c>,
/// or a bridge that maps to a non-existent / duplicated flag.
/// </summary>
public class CapabilityRegistryTests
{
    private static readonly string[] DeclaredCapabilityKeys = CollectConstants(typeof(Capabilities)).ToArray();

    [Fact]
    public void EveryDeclaredCapability_IsRegisteredExactlyOnce()
    {
        foreach (string key in DeclaredCapabilityKeys)
        {
            int count = CapabilityRegistry.All.Count(c => c.Key == key);
            Assert.True(count == 1, $"Capability '{key}' should be registered exactly once, but was {count}.");
        }
    }

    [Fact]
    public void RegistryHasNoUnknownOrDuplicateKeys()
    {
        // No registry entry without a matching declared constant.
        foreach (CapabilityDefinition def in CapabilityRegistry.All)
        {
            Assert.Contains(def.Key, DeclaredCapabilityKeys);
        }

        // No duplicate keys in the registry.
        Assert.Equal(CapabilityRegistry.All.Count, CapabilityRegistry.All.Select(c => c.Key).Distinct().Count());
    }

#pragma warning disable CS0618 // the bridge references the legacy flags on purpose
    [Fact]
    public void LegacyFlagBridge_IsA1To1CoverOfThe13Flags()
    {
        string[] mappedFlags = CapabilityRegistry.All
            .Select(c => c.LegacyFlag)
            .Where(f => f is not null)
            .Select(f => f!)
            .ToArray();

        // Every mapped flag is a real flag.
        foreach (string flag in mappedFlags)
        {
            Assert.Contains(flag, StaffFeatureFlags.All);
        }

        // Bijection: no duplicates, and all 13 flags are covered.
        Assert.Equal(mappedFlags.Length, mappedFlags.Distinct().Count());
        Assert.Equal(
            StaffFeatureFlags.All.OrderBy(f => f).ToArray(),
            mappedFlags.OrderBy(f => f).ToArray());
    }
#pragma warning restore CS0618

    [Fact]
    public void LegacyFlagFor_ThrowsOnUnknownCapability()
    {
        Assert.Throws<ArgumentException>(() => CapabilityRegistry.LegacyFlagFor("not.a.capability"));
    }

    /// <summary>Recursively collects every <c>public const string</c> value from a type and its nested types.</summary>
    private static IEnumerable<string> CollectConstants(Type type)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            {
                yield return (string)field.GetRawConstantValue()!;
            }
        }

        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public))
        {
            foreach (string value in CollectConstants(nested))
            {
                yield return value;
            }
        }
    }
}
