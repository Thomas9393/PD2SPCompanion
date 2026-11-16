using System.Text.Json;
using System.Text.Json.Nodes;

using PD2SPCompanion.Shell.Services.Settings;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// A play policy stored before one of its switches went - in a vault's rules or the settings' Custom modes - reads
/// as it was, the switch ignored: what the grail counted as found (Grail: Finds, FindsAndBought, Anything) went
/// with the editor's finds.
/// </summary>
public sealed class StoredPolicyTests
{
    [Fact]
    public void A_policy_stored_with_what_the_grail_counted_reads_as_it_was()
    {
        var stored = JsonNode.Parse(JsonSerializer.Serialize(PlayPolicy.Legit))!.AsObject();
        stored["Grail"] = "Finds";

        var read = JsonSerializer.Deserialize<PlayPolicy>(stored.ToJsonString());

        Assert.Equal(PlayPolicy.Legit, read);
    }
}
