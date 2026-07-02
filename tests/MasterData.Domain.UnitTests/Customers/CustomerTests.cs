using MasterData.Domain.Customers;
using Shouldly;
using PortalAccessState = MasterData.Domain.PortalAccess.PortalAccessState;

namespace MasterData.Domain.UnitTests.Customers;

public sealed class CustomerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid CountryId = Guid.NewGuid();

    private static Address ValidAddress() =>
        Address.Create("1 Airport Rd", null, "Amman", null, null).Value;

    private static Customer NewCustomer(string iata = "RJ", string? icao = "RJA") =>
        Customer.Create(iata, icao, "Royal Jordanian", CountryId, "ops@rj.com", "+962", null, ValidAddress(), Now).Value;

    [Fact]
    public void Create_normalizes_codes_and_email()
    {
        var result = Customer.Create("  rj ", " rja ", "  Royal Jordanian  ", CountryId, "  OPS@RJ.com ", " +962 ", null, ValidAddress(), Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IataCode.ShouldBe("RJ");
        result.Value.IcaoCode.ShouldBe("RJA");
        result.Value.Name.ShouldBe("Royal Jordanian");
        result.Value.OfficialEmail.ShouldBe("ops@rj.com");
        result.Value.IsActive.ShouldBeTrue();
    }

    [Theory]
    [InlineData("R")]
    [InlineData("RJX")]
    [InlineData("R-")]
    public void Create_with_invalid_iata_fails(string iata)
    {
        var result = Customer.Create(iata, null, "Name", CountryId, null, null, null, ValidAddress(), Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.Customer.IataInvalid");
    }

    [Fact]
    public void Create_without_iata_succeeds()
    {
        var result = Customer.Create(null, "ROJ", "Royal Jet", CountryId, null, null, null, ValidAddress(), Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IataCode.ShouldBeNull();
    }

    [Theory]
    [InlineData("RJ")]
    [InlineData("RJAB")]
    [InlineData("RJ1")]
    public void Create_with_invalid_icao_fails(string icao)
    {
        var result = Customer.Create("RJ", icao, "Name", CountryId, null, null, null, ValidAddress(), Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.Customer.IcaoInvalid");
    }

    [Fact]
    public void Create_with_invalid_official_email_fails()
    {
        var result = Customer.Create("RJ", null, "Name", CountryId, "not-an-email", null, null, ValidAddress(), Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.Customer.OfficialEmailInvalid");
    }

    [Fact]
    public void AddContact_rejects_duplicate_email()
    {
        var customer = NewCustomer();
        customer.AddContact("Alice", null, "a@rj.com", null, Now).IsSuccess.ShouldBeTrue();

        var duplicate = customer.AddContact("Alice 2", null, "A@RJ.com", null, Now);

        duplicate.IsFailure.ShouldBeTrue();
        duplicate.Error.Code.ShouldBe("MasterData.CustomerContact.EmailNotUnique");
    }

    [Fact]
    public void Contact_portal_suspension_keeps_link_for_restore()
    {
        var customer = NewCustomer();
        var contact = customer.AddContact("Alice", null, "alice@rj.com", null, Now).Value;
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        contact.RequestPortalAccess(correlationId, Now);
        contact.LinkUser(userId, correlationId, Now.AddMinutes(1));

        contact.SuspendPortal(Now.AddMinutes(2));

        contact.LinkedUserId.ShouldBe(userId);
        contact.PortalState.ShouldBe(PortalAccessState.Suspended);
        contact.PortalCorrelationId.ShouldBe(correlationId);
    }

    [Fact]
    public void Contact_portal_activation_marks_same_linked_user_active()
    {
        var customer = NewCustomer();
        var contact = customer.AddContact("Alice", null, "alice@rj.com", null, Now).Value;
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        contact.RequestPortalAccess(correlationId, Now);
        contact.LinkUser(userId, correlationId, Now.AddMinutes(1));
        contact.MarkPortalActive(userId, Now.AddMinutes(2));

        contact.LinkedUserId.ShouldBe(userId);
        contact.PortalState.ShouldBe(PortalAccessState.Active);
        contact.PortalFailureReason.ShouldBeNull();
    }

    [Fact]
    public void Contact_portal_restore_can_mark_same_linked_user_invited_again()
    {
        var customer = NewCustomer();
        var contact = customer.AddContact("Alice", null, "alice@rj.com", null, Now).Value;
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        contact.RequestPortalAccess(correlationId, Now);
        contact.LinkUser(userId, correlationId, Now.AddMinutes(1));
        contact.SuspendPortal(Now.AddMinutes(2));
        contact.MarkPortalInvited(userId, Now.AddMinutes(3));

        contact.LinkedUserId.ShouldBe(userId);
        contact.PortalState.ShouldBe(PortalAccessState.Invited);
        contact.PortalFailureReason.ShouldBeNull();
    }

    [Fact]
    public void Contact_portal_activation_ignores_wrong_or_removed_link()
    {
        var customer = NewCustomer();
        var contact = customer.AddContact("Alice", null, "alice@rj.com", null, Now).Value;
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        contact.RequestPortalAccess(correlationId, Now);
        contact.LinkUser(userId, correlationId, Now.AddMinutes(1));
        contact.MarkPortalActive(Guid.NewGuid(), Now.AddMinutes(2));
        contact.PortalState.ShouldBe(PortalAccessState.Invited);

        customer.RemoveContact(contact.Id, releaseLink: false, Now.AddMinutes(3)).IsSuccess.ShouldBeTrue();
        contact.MarkPortalActive(userId, Now.AddMinutes(4));

        contact.PortalState.ShouldBe(PortalAccessState.Invited);
    }

    [Fact]
    public void Permanent_contact_removal_unlinks_portal_user_and_clears_retry_state()
    {
        var customer = NewCustomer();
        var contact = customer.AddContact("Alice", null, "alice@rj.com", null, Now).Value;
        var correlationId = Guid.NewGuid();
        contact.RequestPortalAccess(correlationId, Now);
        contact.MarkPortalFailed(correlationId, "duplicate email", Now.AddMinutes(1));

        var removed = customer.RemoveContact(contact.Id, releaseLink: true, Now.AddMinutes(2));

        removed.IsSuccess.ShouldBeTrue();
        removed.Value.IsActive.ShouldBeFalse();
        removed.Value.LinkedUserId.ShouldBeNull();
        removed.Value.PortalState.ShouldBe(PortalAccessState.None);
        removed.Value.PortalCorrelationId.ShouldBeNull();
        removed.Value.PortalFailureReason.ShouldBeNull();
    }

    [Fact]
    public void ReconcileContacts_adds_updates_and_deactivates_by_id()
    {
        var customer = NewCustomer();
        customer.AddContact("Alice", "Manager", "alice@rj.com", null, Now);
        customer.AddContact("Bob", null, "bob@rj.com", null, Now);

        var alice = customer.Contacts.Single(c => c.Email == "alice@rj.com");

        // Keep+rename Alice, drop Bob (omitted), add Carol.
        var result = customer.ReconcileContacts(
        [
            new ContactReconciliationItem(alice.Id, "Alice Updated", "Director", "alice@rj.com", "+1"),
            new ContactReconciliationItem(null, "Carol", null, "carol@rj.com", null)
        ], Now.AddDays(1));

        result.IsSuccess.ShouldBeTrue();

        var active = customer.Contacts.Where(c => c.IsActive).ToList();
        active.Count.ShouldBe(2);
        active.ShouldContain(c => c.Email == "alice@rj.com" && c.Name == "Alice Updated" && c.JobTitle == "Director");
        active.ShouldContain(c => c.Email == "carol@rj.com");

        customer.Contacts.Single(c => c.Email == "bob@rj.com").IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ReconcileContacts_rejects_duplicate_email_in_payload()
    {
        var customer = NewCustomer();

        var result = customer.ReconcileContacts(
        [
            new ContactReconciliationItem(null, "A", null, "dup@rj.com", null),
            new ContactReconciliationItem(null, "B", null, "DUP@rj.com", null)
        ], Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.CustomerContact.EmailNotUnique");
    }

    [Fact]
    public void ReconcileContacts_with_unknown_id_fails()
    {
        var customer = NewCustomer();

        var result = customer.ReconcileContacts(
        [
            new ContactReconciliationItem(Guid.NewGuid(), "Ghost", null, "ghost@rj.com", null)
        ], Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.CustomerContact.NotFound");
    }

    [Fact]
    public void SetLogo_stores_reference()
    {
        var customer = NewCustomer();

        customer.SetLogo("customer-logos/abc.png", Now.AddDays(1));

        customer.LogoFileReference.ShouldBe("customer-logos/abc.png");
    }

    [Fact]
    public void Address_allows_partial_or_blank_legacy_values()
    {
        Address.Create(null, null, "City", null, null).IsSuccess.ShouldBeTrue();
        Address.Create("Line 1", null, null, null, null).IsSuccess.ShouldBeTrue();
        Address.Create(null, null, null, null, null).IsSuccess.ShouldBeTrue();
        Address.Create("Line 1", "Suite 2", "City", "Region", "11118").IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Address_still_enforces_field_lengths()
    {
        Address.Create(new string('x', 201), null, null, null, null).IsFailure.ShouldBeTrue();
        Address.Create(null, null, new string('x', 101), null, null).IsFailure.ShouldBeTrue();
    }
}
