using Xunit;
using Zenith.Gameplay.Permissions;

namespace Zenith.Tests;

public sealed class PermissionAuthorityTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string Open = "test.open";
    private const string Admin = "test.admin";
    private const string Never = "test.never";

    private static PermissionRegistry Registry()
    {
        var registry = new PermissionRegistry();
        registry.Register(Open, PermissionDefault.Everyone);
        registry.Register(Admin, PermissionDefault.Operator);
        registry.Register(Never, PermissionDefault.Nobody);
        return registry;
    }

    private static PermissionAuthority Authority(
        string[]? operators = null,
        Dictionary<string, List<string>>? grants = null,
        Dictionary<string, List<string>>? denies = null,
        bool trustSelfAsserted = false)
    {
        var registry = Registry();
        return new PermissionAuthority(registry, PermissionPolicy.Create(registry, operators ?? [], grants, denies, trustSelfAsserted));
    }

    private static Principal Authenticated(Guid who) => new(who, IdentityTrust.Authenticated, 1);

    [Fact]
    public void Defaults_apply_to_players_nobody_has_configured()
    {
        var authority = Authority();
        var player = Authenticated(Alice);

        Assert.True(authority.Has(player, Open));
        Assert.False(authority.Has(player, Admin));
        Assert.False(authority.Has(player, Never));
    }

    [Fact]
    public void An_unknown_node_is_denied_even_for_an_operator()
    {
        var authority = Authority(operators: [Alice.ToString()]);

        Assert.Equal(PermissionDecision.DeniedUnknownNode, authority.Evaluate(Authenticated(Alice), "test.typo"));
    }

    [Fact]
    public void An_authenticated_operator_holds_operator_nodes_but_not_nobody_nodes()
    {
        var authority = Authority(operators: [Alice.ToString()]);

        Assert.True(authority.Has(Authenticated(Alice), Admin));
        Assert.False(authority.Has(Authenticated(Alice), Never));
        Assert.False(authority.Has(Authenticated(Bob), Admin));
    }

    [Fact]
    public void Precedence_is_deny_over_grant_over_default()
    {
        var authority = Authority(
            grants: new() { [Alice.ToString()] = [Admin, Never, Open], [Bob.ToString()] = [Never] },
            denies: new() { [Alice.ToString()] = [Open, Never] });

        Assert.False(authority.Has(Authenticated(Alice), Open));   // explicit deny beats the Everyone default
        Assert.False(authority.Has(Authenticated(Alice), Never));  // explicit deny beats an explicit grant
        Assert.True(authority.Has(Authenticated(Alice), Admin));   // grant beats an Operator default
        Assert.True(authority.Has(Authenticated(Bob), Never));     // grant beats a Nobody default
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Operator_status_and_grants_do_not_apply_to_identities_the_server_cannot_vouch_for(bool ephemeral)
    {
        var authority = Authority(
            operators: [Alice.ToString()],
            grants: new() { [Alice.ToString()] = [Never] });
        var claimant = new Principal(Alice, ephemeral ? IdentityTrust.Ephemeral : IdentityTrust.SelfAsserted, 7);

        Assert.False(authority.Has(claimant, Admin));
        Assert.False(authority.Has(claimant, Never));
        Assert.True(authority.Has(claimant, Open));
    }

    [Fact]
    public void Explicit_denies_apply_to_every_trust_level()
    {
        var authority = Authority(denies: new() { [Alice.ToString()] = [Open] });

        Assert.False(authority.Has(new Principal(Alice, IdentityTrust.Ephemeral, 1), Open));
        Assert.False(authority.Has(new Principal(Alice, IdentityTrust.SelfAsserted, 1), Open));
    }

    [Fact]
    public void The_development_switch_trusts_self_asserted_but_never_ephemeral_identities()
    {
        var authority = Authority(operators: [Alice.ToString()], trustSelfAsserted: true);

        Assert.True(authority.Has(new Principal(Alice, IdentityTrust.SelfAsserted, 1), Admin));
        Assert.False(authority.Has(new Principal(Alice, IdentityTrust.Ephemeral, 1), Admin));
    }

    [Fact]
    public void Replacing_the_policy_revokes_privilege_for_every_later_check()
    {
        var registry = Registry();
        var authority = new PermissionAuthority(registry, PermissionPolicy.Create(registry, [Alice.ToString()], null, null, false));
        var alice = Authenticated(Alice);
        Assert.True(authority.Has(alice, Admin));

        authority.Replace(PermissionPolicy.Empty);

        Assert.False(authority.Has(alice, Admin));
    }

    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("")]
    public void Configuration_naming_a_bad_identity_is_rejected(string subject)
    {
        var registry = Registry();
        Assert.Throws<InvalidOperationException>(() => PermissionPolicy.Create(registry, [subject], null, null, false));
        Assert.Throws<InvalidOperationException>(() =>
            PermissionPolicy.Create(registry, [], new Dictionary<string, List<string>> { [subject] = [Open] }, null, false));
    }

    [Fact]
    public void Configuration_naming_an_unknown_node_is_rejected()
    {
        var registry = Registry();
        var ex = Assert.Throws<InvalidOperationException>(() => PermissionPolicy.Create(
            registry, [], new Dictionary<string, List<string>> { [Alice.ToString()] = ["test.typo"] }, null, false));
        Assert.Contains("test.typo", ex.Message);
    }

    [Theory]
    [InlineData("zenith.command.gamemode.self", true)]
    [InlineData("a", true)]
    [InlineData("a.*", false)]
    [InlineData("A.b", false)]
    [InlineData("a..b", false)]
    [InlineData("", false)]
    [InlineData("a b", false)]
    public void Nodes_are_exact_dotted_lowercase_words(string node, bool wellFormed) =>
        Assert.Equal(wellFormed, PermissionRegistry.IsWellFormed(node));

    [Fact]
    public void A_frozen_registry_refuses_new_nodes_and_duplicates_are_rejected()
    {
        var registry = Registry();
        Assert.Throws<InvalidOperationException>(() => registry.Register(Open, PermissionDefault.Everyone));
        registry.Freeze();
        Assert.Throws<InvalidOperationException>(() => registry.Register("test.late", PermissionDefault.Everyone));
    }

    [Fact]
    public void Every_built_in_node_registers()
    {
        var registry = new PermissionRegistry();
        PermissionNodes.RegisterBuiltIns(registry);
        Assert.Contains(PermissionNodes.GameModeOthers, registry.Nodes);
    }
}
