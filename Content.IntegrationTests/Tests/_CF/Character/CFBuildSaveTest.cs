using System.Text.Json;
using Content.IntegrationTests.Fixtures;
using Content.Server.Database;
using Content.Shared.CF.Preferences;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>A build goes through the same steps here as it does on its way to the database and back.</summary>
[TestFixture]
public sealed class CFBuildSaveTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    private async Task<CFCharacterBuildData> SaveAndLoad(CFCharacterBuildData build)
    {
        CFCharacterBuildData loaded = default!;
        await Server.WaitPost(() =>
        {
            var serialization = Server.ResolveDependency<ISerializationManager>();
            var node = serialization.WriteValue(build, alwaysWrite: true, notNullableOverride: true);
            using var document = JsonSerializer.SerializeToDocument(node.ToJsonNode());
            loaded = serialization.Read<CFCharacterBuildData>(document.RootElement.ToDataNode(), notNullableOverride: true);
        });

        return loaded;
    }

    [Test]
    public async Task AHalfMadeCharacterIsNotLost()
    {
        var build = new CFCharacterBuildData { Class = "Rogue", Background = "Acolyte" };
        build.ClassChoices.Add("Thief");
        build.ClassTrainedSkills.Add("Athletics");

        var loaded = await SaveAndLoad(build);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Equals(build), "Nothing changes on the way through.");
            Assert.That(loaded.BackgroundBoost, Is.Null, "An unpicked boost stays unpicked.");
            Assert.That(loaded.Heritage, Is.Null, "An unpicked option is empty, not an option called null.");
        });
    }

    [Test]
    public async Task AFinishedCharacterStillLoads()
    {
        var build = new CFCharacterBuildData
        {
            Class = "Rogue",
            BackgroundBoost = CFAbility.Dexterity,
            BackgroundFreeBoost = CFAbility.Charisma,
        };
        build.FreeBoosts.Add(CFAbility.Strength);

        Assert.That((await SaveAndLoad(build)).Equals(build));
    }

    [Test]
    public async Task AnEmptyBuildLoads()
    {
        var build = new CFCharacterBuildData();
        Assert.That((await SaveAndLoad(build)).Equals(build));
    }
}
