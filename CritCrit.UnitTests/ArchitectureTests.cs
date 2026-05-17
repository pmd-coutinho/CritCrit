using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace CritCrit.UnitTests;

/// <summary>
/// Architecture rules — enforce the Platform/Org slice boundary at test time so
/// future contributors don't accidentally drag Org/Handlers into Platform code
/// or pollute Org.Domain with infrastructure types.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(typeof(CritCrit.Api.Org.Domain.OrgNodeId).Assembly)
        .Build();

    [Fact]
    public void OrgDomain_is_pure_and_depends_on_no_other_CritCrit_namespace()
    {
        // Org.Domain is the deepest layer: events, value-type IDs, read-model docs,
        // rules, public-id format. It must never reach into Platform or any other
        // Org sub-namespace — otherwise we cycle the slice graph.
        Types()
            .That().ResideInNamespace("CritCrit.Api.Org.Domain")
            .Should()
            .NotDependOnAny(
                Types().That().ResideInNamespaceMatching(@"CritCrit\.Api\.(Org\.(Handlers|Projections|Infrastructure|Identity|Invitations|Validators|Endpoints|Auth)|Platform).*"))
            .Because("Org.Domain must stay pure so every other slice can depend on it freely.")
            .Check(Architecture);
    }

    [Fact]
    public void Platform_only_reaches_into_Org_via_Auth_and_Domain()
    {
        // Platform/* may depend on Org.Auth (for ActorContext, ActorContextResolver)
        // and Org.Domain (for OrgNodeReadModel, OrgRole, SubjectId). It must NOT
        // depend on Org.Handlers, Projections, Infrastructure, Identity, Invitations,
        // Validators or Endpoints — those are slice-private.
        Types()
            .That().ResideInNamespaceMatching(@"CritCrit\.Api\.Platform\..*")
            .Should()
            .NotDependOnAny(
                Types().That().ResideInNamespaceMatching(@"CritCrit\.Api\.Org\.(Handlers|Projections|Infrastructure|Identity|Invitations|Validators|Endpoints).*"))
            .Because("Platform must remain slice-agnostic — Org sub-slices are private.")
            .Check(Architecture);
    }

    [Fact]
    public void Domain_events_are_sealed_records()
    {
        // OrgEvents/InvitationMessages style: every domain event should be a sealed
        // record so equality, immutability and exhaustive-match semantics hold.
        // Catches a future contributor adding a mutable event class.
        Classes()
            .That().ResideInNamespace("CritCrit.Api.Org.Domain")
            .And().HaveNameEndingWith("Created")
            .Or().HaveNameEndingWith("Archived")
            .Or().HaveNameEndingWith("Restored")
            .Or().HaveNameEndingWith("Renamed")
            .Or().HaveNameEndingWith("Moved")
            .Or().HaveNameEndingWith("HardDeleted")
            .Should().BeSealed()
            .Because("Event records must be sealed to preserve equality and immutability invariants.")
            .Check(Architecture);
    }

    [Fact]
    public void Validators_live_in_the_Validators_namespace()
    {
        // FluentValidation `AbstractValidator<T>` implementations all live in
        // CritCrit.Api.Org.Validators. Stops a contributor sticking one inside
        // a handler file by accident.
        Classes()
            .That().HaveNameEndingWith("Validator")
            .And().ResideInNamespaceMatching(@"CritCrit\.Api\..*")
            .Should().ResideInNamespace("CritCrit.Api.Org.Validators")
            .Because("Validators are conventionally grouped under Org.Validators.")
            .Check(Architecture);
    }

    [Fact]
    public void Projections_live_in_the_Projections_namespace()
    {
        // Same convention for Marten projections — keep them in one folder so
        // new slices know where to drop theirs.
        Classes()
            .That().HaveNameEndingWith("Projection")
            .And().ResideInNamespaceMatching(@"CritCrit\.Api\..*")
            .Should().ResideInNamespaceMatching(@"CritCrit\.Api\..*\.Projections")
            .Because("Marten projections live in *.Projections per slice.")
            .Check(Architecture);
    }

    [Fact]
    public void Config_feature_slice_does_not_leak_into_other_features()
    {
        // Config is its own Org/Features subfolder. Other features (Brands,
        // OrgNodes, Invitations, etc.) must not depend on Config types so the
        // slice stays surgically removable / promotable to its own service.
        Types()
            .That().ResideInNamespaceMatching(@"CritCrit\.Api\.Org\.Features\.(Brands|OrgNodes|AccessGrants|Subjects|Invitations|Owners|Audit)")
            .Should()
            .NotDependOnAny(
                Types().That().ResideInNamespaceMatching(@"CritCrit\.Api\.Org\.Features\.Config"))
            .Because("Config is its own slice; other features should not reach into it.")
            .WithoutRequiringPositiveResults()
            .Check(Architecture);
    }

    [Fact]
    public void Config_domain_events_are_sealed_records()
    {
        // Same invariant as the org events, scoped to the config types added
        // in this plan. Belt-and-suspenders against a future mutable event.
        Classes()
            .That().ResideInNamespace("CritCrit.Api.Org.Domain")
            .And().HaveNameStartingWith("ConfigSchema")
            .Or().HaveNameStartingWith("ConfigAssignment")
            .Or().HaveNameStartingWith("ConfigNodeValue")
            .Should().BeSealed()
            .Because("Config event records must be sealed for immutability.")
            .Check(Architecture);
    }
}
