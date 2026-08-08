using Xunit;

namespace Puffinote.TestSupport;

/// <summary>
/// Proves <see cref="HermeticProfile"/> actually took, in every test assembly that compiles it.
/// </summary>
/// <remarks>
/// The redirect is the kind of safety net that fails silently: if a runtime stopped consulting these
/// variables, or the module initializer stopped running, nothing would break — tests would simply go back
/// to reading and writing the developer's real profile, and the damage would show up later as an
/// unrelated flake on one machine. So it is asserted rather than assumed, and asserted here so it is
/// checked once per test process rather than once per repository.
/// </remarks>
public class HermeticProfileTests
{
    [Fact]
    public void Every_user_data_location_resolves_inside_the_throwaway_profile()
    {
        var root = HermeticProfile.Root;
        Assert.True(Directory.Exists(root), $"the throwaway profile should exist: {root}");

        // The two ways the apps in this family ask where the user lives. Both have to land inside the
        // scratch root — GetFolderPath in particular resolves differently per OS, which is exactly the
        // thing worth pinning down rather than reasoning about.
        Assert.Equal(root, Environment.GetEnvironmentVariable("HOME"));

        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.UserProfile,
                     Environment.SpecialFolder.ApplicationData,
                     Environment.SpecialFolder.LocalApplicationData,
                 })
        {
            var path = Environment.GetFolderPath(folder);
            Assert.True(
                path.StartsWith(root, StringComparison.Ordinal),
                $"{folder} resolved to {path}, which is outside the throwaway profile {root}");
        }
    }
}
