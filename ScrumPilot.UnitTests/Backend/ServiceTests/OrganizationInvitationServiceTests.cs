using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class OrganizationInvitationServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ScrumPilotContext _context;
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IInvitationAcceptanceUserLookup _userLookup =
        Substitute.For<IInvitationAcceptanceUserLookup>();
    private readonly IOrganizationAccessService _access = Substitute.For<IOrganizationAccessService>();
    private readonly FakeInvitationEmailSender _sender = new();
    private readonly TestTimeProvider _clock =
        new(new DateTimeOffset(2026, 10, 3, 4, 0, 0, TimeSpan.Zero));
    private readonly OrganizationInvitationService _service;
    private int _organizationId;

    public OrganizationInvitationServiceTests()
    {
        _connection.Open();
        _context = new ScrumPilotContext(
            new DbContextOptionsBuilder<ScrumPilotContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _currentUser.UserId.Returns("owner");
        _currentUser.Email.Returns("owner@example.com");
        _userLookup.FindByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var user = _context.Users.Find(call.ArgAt<string>(0));
                return user is null
                    ? null
                    : new InvitationAcceptanceUser(user.Email, user.EmailConfirmed);
            });
        _service = new(
            new OrganizationInvitationRepository(_context),
            _currentUser,
            _userLookup,
            _access,
            _sender,
            _clock);
    }

    [Fact]
    public async Task InviteAsync_NormalizesEmailHashesTokenAndPersistsBeforeSending()
    {
        await SeedAsync();

        var result = await _service.InviteAsync(
            _organizationId,
            new("  New.Member@example.com  ", OrganizationRole.Member));

        var stored = await _context.OrganizationInvitations.SingleAsync();
        Assert.Equal("NEW.MEMBER@EXAMPLE.COM", stored.NormalizedEmail);
        Assert.Equal(64, stored.TokenHash.Length);
        Assert.DoesNotContain(_sender.Token!, stored.TokenHash);
        Assert.Equal(SHA256.HashData(Base64UrlDecode(_sender.Token!)), Convert.FromHexString(stored.TokenHash));
        Assert.True(_sender.InvitationWasPersisted);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, stored.LastSentAt);
        Assert.Null(stored.DeliveryError);
        Assert.Equal(stored.OrganizationInvitationId, result.OrganizationInvitationId);
    }

    [Fact]
    public async Task InviteAsync_NonOwnerIsForbidden()
    {
        await SeedAsync(ownerAccess: false);

        await Assert.ThrowsAsync<OrganizationForbiddenException>(() =>
            _service.InviteAsync(
                _organizationId,
                new("member@example.com", OrganizationRole.Member)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("member@example.com", 99)]
    public async Task InviteAsync_InvalidRequestIsRejected(string email, int role = 1)
    {
        await SeedAsync();

        await Assert.ThrowsAsync<OrganizationValidationException>(() =>
            _service.InviteAsync(_organizationId, new(email, (OrganizationRole)role)));
    }

    [Fact]
    public async Task InviteAsync_DeliveryFailureRemainsPendingAndRecordsError()
    {
        await SeedAsync();
        _sender.Error = "SendGrid returned 503.";

        await Assert.ThrowsAsync<InvitationDeliveryException>(() =>
            _service.InviteAsync(
                _organizationId,
                new("member@example.com", OrganizationRole.Member)));

        var stored = await _context.OrganizationInvitations.SingleAsync();
        Assert.Equal(OrganizationInvitationStatus.Pending, stored.Status);
        Assert.Equal("SendGrid returned 503.", stored.DeliveryError);
        Assert.Null(stored.LastSentAt);
    }

    [Fact]
    public async Task InviteAsync_ReplacesExistingPendingInvitation()
    {
        await SeedAsync();
        await _service.InviteAsync(
            _organizationId,
            new("member@example.com", OrganizationRole.Member));
        var oldToken = _sender.Token;

        await _service.InviteAsync(
            _organizationId,
            new(" MEMBER@example.com ", OrganizationRole.Owner));

        var invitations = await _context.OrganizationInvitations.OrderBy(x => x.OrganizationInvitationId).ToListAsync();
        Assert.Equal(OrganizationInvitationStatus.Revoked, invitations[0].Status);
        Assert.Equal(OrganizationInvitationStatus.Pending, invitations[1].Status);
        _currentUser.UserId.Returns("member");
        _currentUser.Email.Returns("member@example.com");
        await Assert.ThrowsAsync<OrganizationNotFoundException>(() =>
            _service.AcceptAsync(new(oldToken!)));
    }

    [Fact]
    public async Task ResendAsync_RotatesTokenSoOnlyNewestCanBeAccepted()
    {
        await SeedAsync();
        var invitation = await _service.InviteAsync(
            _organizationId,
            new("member@example.com", OrganizationRole.Member));
        var oldToken = _sender.Token;

        await _service.ResendAsync(_organizationId, invitation.OrganizationInvitationId);
        var newToken = _sender.Token;

        _currentUser.UserId.Returns("member");
        _currentUser.Email.Returns("member@example.com");
        await Assert.ThrowsAsync<OrganizationNotFoundException>(() =>
            _service.AcceptAsync(new(oldToken!)));
        await _service.AcceptAsync(new(newToken!));
        Assert.True(await _context.OrganizationMemberships.AnyAsync(x =>
            x.UserId == "member" && x.Role == OrganizationRole.Member));
    }

    [Fact]
    public async Task AcceptAsync_RejectsWrongEmailExpiredRevokedAndSecondUse()
    {
        await SeedAsync();
        var invitation = await _service.InviteAsync(
            _organizationId,
            new("member@example.com", OrganizationRole.Member));
        var token = _sender.Token!;
        _currentUser.UserId.Returns("member");
        var member = await _context.Users.FindAsync("member");
        member!.Email = "wrong@example.com";
        await _context.SaveChangesAsync();
        await Assert.ThrowsAsync<OrganizationForbiddenException>(() => _service.AcceptAsync(new(token)));

        member.Email = "member@example.com";
        await _context.SaveChangesAsync();
        _clock.Advance(TimeSpan.FromHours(73));
        await Assert.ThrowsAsync<OrganizationConflictException>(() => _service.AcceptAsync(new(token)));
        Assert.Equal(
            OrganizationInvitationStatus.Expired,
            (await _context.OrganizationInvitations.FindAsync(invitation.OrganizationInvitationId))!.Status);

        _clock.Advance(TimeSpan.FromHours(-73));
        _currentUser.UserId.Returns("owner");
        _currentUser.Email.Returns("owner@example.com");
        invitation = await _service.InviteAsync(
            _organizationId,
            new("member@example.com", OrganizationRole.Member));
        token = _sender.Token!;
        await _service.RevokeAsync(_organizationId, invitation.OrganizationInvitationId);
        _currentUser.UserId.Returns("member");
        _currentUser.Email.Returns("member@example.com");
        await Assert.ThrowsAsync<OrganizationNotFoundException>(() => _service.AcceptAsync(new(token)));

        _currentUser.UserId.Returns("owner");
        _currentUser.Email.Returns("owner@example.com");
        invitation = await _service.InviteAsync(
            _organizationId,
            new("member@example.com", OrganizationRole.Member));
        token = _sender.Token!;
        _currentUser.UserId.Returns("member");
        _currentUser.Email.Returns("member@example.com");
        await _service.AcceptAsync(new(token));
        await Assert.ThrowsAsync<OrganizationNotFoundException>(() => _service.AcceptAsync(new(token)));
    }

    [Fact]
    public async Task AcceptAsync_ExistingMemberDoesNotGetPromoted()
    {
        await SeedAsync();
        _context.OrganizationMemberships.Add(new()
        {
            OrganizationId = _organizationId,
            UserId = "member",
            Role = OrganizationRole.Member,
            JoinedAt = _clock.GetUtcNow().UtcDateTime
        });
        await _context.SaveChangesAsync();
        await _service.InviteAsync(
            _organizationId,
            new("member@example.com", OrganizationRole.Owner));
        _currentUser.UserId.Returns("member");
        _currentUser.Email.Returns("member@example.com");

        await Assert.ThrowsAsync<OrganizationConflictException>(() =>
            _service.AcceptAsync(new(_sender.Token!)));

        Assert.Equal(
            OrganizationRole.Member,
            (await _context.OrganizationMemberships.SingleAsync(x => x.UserId == "member")).Role);
    }

    [Fact]
    public async Task AcceptAsync_UsesConfirmedDatabaseEmailInsteadOfStaleJwtEmail()
    {
        await SeedAsync();
        await _service.InviteAsync(
            _organizationId,
            new("member@example.com", OrganizationRole.Member));
        _currentUser.UserId.Returns("member");
        _currentUser.Email.Returns("stale@example.com");

        await _service.AcceptAsync(new(_sender.Token!));

        Assert.True(await _context.OrganizationMemberships.AnyAsync(x =>
            x.OrganizationId == _organizationId && x.UserId == "member"));
    }

    [Fact]
    public async Task AcceptAsync_UnconfirmedDatabaseEmailIsForbidden()
    {
        await SeedAsync();
        await _service.InviteAsync(
            _organizationId,
            new("member@example.com", OrganizationRole.Member));
        var member = await _context.Users.FindAsync("member");
        member!.EmailConfirmed = false;
        await _context.SaveChangesAsync();
        _currentUser.UserId.Returns("member");

        await Assert.ThrowsAsync<OrganizationForbiddenException>(() =>
            _service.AcceptAsync(new(_sender.Token!)));
    }

    [Fact]
    public async Task AcceptAsync_ChangedDatabaseEmailIsForbidden()
    {
        await SeedAsync();
        await _service.InviteAsync(
            _organizationId,
            new("member@example.com", OrganizationRole.Member));
        var member = await _context.Users.FindAsync("member");
        member!.Email = "changed@example.com";
        member.NormalizedEmail = "CHANGED@EXAMPLE.COM";
        await _context.SaveChangesAsync();
        _currentUser.UserId.Returns("member");

        await Assert.ThrowsAsync<OrganizationForbiddenException>(() =>
            _service.AcceptAsync(new(_sender.Token!)));
    }

    [Theory]
    [InlineData("invite")]
    [InlineData("resend")]
    [InlineData("accept")]
    public async Task MutationConcurrencyFailureIsMappedToConflict(string operation)
    {
        var repository = Substitute.For<IOrganizationInvitationRepository>();
        var currentUser = Substitute.For<ICurrentUser>();
        var lookup = Substitute.For<IInvitationAcceptanceUserLookup>();
        var access = Substitute.For<IOrganizationAccessService>();
        currentUser.UserId.Returns("owner");
        access.IsOrganizationOwnerAsync("owner", 5, Arg.Any<CancellationToken>())
            .Returns(true);
        lookup.FindByIdAsync("owner", Arg.Any<CancellationToken>())
            .Returns(new InvitationAcceptanceUser("owner@example.com", true));
        var failure = new OrganizationInvitationConcurrencyException(
            "concurrent",
            new InvalidOperationException());
        repository.ReplacePendingAsync(
                Arg.Any<OrganizationInvitation>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<OrganizationInvitation>>(_ => throw failure);
        repository.GetAsync(5, 9, Arg.Any<CancellationToken>())
            .Returns(new OrganizationInvitation
            {
                OrganizationInvitationId = 9,
                OrganizationId = 5,
                Email = "member@example.com",
                NormalizedEmail = "MEMBER@EXAMPLE.COM",
                TokenHash = new string('0', 64),
                InvitedByUserId = "owner",
                Role = OrganizationRole.Member,
                Status = OrganizationInvitationStatus.Pending
            });
        repository.ReplaceAsync(
                5,
                9,
                Arg.Any<OrganizationInvitation>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<OrganizationInvitation?>>(_ => throw failure);
        repository.AcceptAsync(
                Arg.Any<string>(),
                "owner",
                "OWNER@EXAMPLE.COM",
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<InvitationAcceptanceResult>>(_ => throw failure);
        var service = new OrganizationInvitationService(
            repository,
            currentUser,
            lookup,
            access,
            _sender,
            _clock);

        Task Action() => operation switch
        {
            "invite" => service.InviteAsync(
                5,
                new("member@example.com", OrganizationRole.Member)),
            "resend" => service.ResendAsync(5, 9),
            _ => service.AcceptAsync(new(
                Convert.ToBase64String(new byte[32])
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_')))
        };

        await Assert.ThrowsAsync<OrganizationConflictException>(Action);
    }

    [Theory]
    [InlineData("%%%")]
    [InlineData("abcde")]
    [InlineData("YWJj=")]
    public async Task AcceptAsync_MalformedBase64UrlTokenIsValidationFailure(string token)
    {
        await SeedAsync();
        _currentUser.UserId.Returns("member");

        await Assert.ThrowsAsync<OrganizationValidationException>(() =>
            _service.AcceptAsync(new(token)));
    }

    [Fact]
    public async Task ListAsync_RedactsTokenHashAndRequiresOwner()
    {
        await SeedAsync();
        await _service.InviteAsync(
            _organizationId,
            new("member@example.com", OrganizationRole.Member));

        var result = Assert.Single(await _service.ListAsync(_organizationId));

        Assert.Null(result.GetType().GetProperty("TokenHash"));
        Assert.Null(result.GetType().GetProperty("Token"));
        _access.IsOrganizationOwnerAsync("owner", _organizationId, Arg.Any<CancellationToken>())
            .Returns(false);
        await Assert.ThrowsAsync<OrganizationForbiddenException>(() =>
            _service.ListAsync(_organizationId));
    }

    private async Task SeedAsync(bool ownerAccess = true)
    {
        if (_organizationId != 0)
        {
            return;
        }
        _context.Users.AddRange(
            User("owner", "owner@example.com"),
            User("member", "member@example.com"));
        var organization = new Organization
        {
            Name = "Pirate Forge",
            NormalizedName = "PIRATE FORGE",
            CreatedAt = _clock.GetUtcNow().UtcDateTime
        };
        _context.Organizations.Add(organization);
        await _context.SaveChangesAsync();
        _organizationId = organization.OrganizationId;
        _context.OrganizationMemberships.Add(new()
        {
            OrganizationId = _organizationId,
            UserId = "owner",
            Role = OrganizationRole.Owner,
            JoinedAt = _clock.GetUtcNow().UtcDateTime
        });
        await _context.SaveChangesAsync();
        _access.IsOrganizationOwnerAsync("owner", _organizationId, Arg.Any<CancellationToken>())
            .Returns(ownerAccess);
        _sender.InvitationPersisted = async () =>
            await _context.OrganizationInvitations.AnyAsync();
    }

    private static ApplicationUser User(string id, string email) => new()
    {
        Id = id,
        UserName = id,
        NormalizedUserName = id.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true
    };

    private static byte[] Base64UrlDecode(string token)
    {
        var value = token.Replace('-', '+').Replace('_', '/');
        value = value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=');
        return Convert.FromBase64String(value);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class FakeInvitationEmailSender : IInvitationEmailSender
    {
        public string? Token { get; private set; }
        public string? Error { get; set; }
        public bool InvitationWasPersisted { get; private set; }
        public Func<Task<bool>>? InvitationPersisted { get; set; }

        public async Task SendAsync(string email, string token, CancellationToken cancellationToken = default)
        {
            Token = token;
            InvitationWasPersisted = InvitationPersisted is not null && await InvitationPersisted();
            if (Error is not null)
            {
                throw new InvitationDeliveryException(Error);
            }
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan amount) => _utcNow += amount;
    }
}
