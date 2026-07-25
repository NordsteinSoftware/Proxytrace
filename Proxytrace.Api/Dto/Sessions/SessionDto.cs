using ISession = Proxytrace.Domain.Session.ISession;

namespace Proxytrace.Api.Dto.Sessions;

public record SessionDto(
    Guid Id,
    Guid ProjectId,
    string ExternalKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    int TraceCount,
    long TotalTokens)
{
    public static SessionDto From(ISession session)
        => new(
            session.Id,
            session.ProjectId,
            session.ExternalKey,
            session.CreatedAt,
            session.LastActivityAt,
            session.TraceCount,
            session.TotalTokens);
}
