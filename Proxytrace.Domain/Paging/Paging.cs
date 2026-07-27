namespace Proxytrace.Domain.Paging;

public static class Paging
{
    private const int MaxPageSize = 100;

    public static (int Page, int PageSize) Clamp(int page, int pageSize)
        => (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));

    /// <summary>
    /// The number of rows to skip for a 1-based <paramref name="page"/>, saturating at
    /// <see cref="int.MaxValue"/> instead of overflowing.
    ///
    /// A plain <c>(page - 1) * pageSize</c> overflows to a <em>negative</em> int for a large page
    /// (e.g. page 2147483647 × pageSize 100), and both <c>Enumerable.Skip</c> and
    /// <c>Queryable.Skip</c> treat a negative count as zero — so the caller silently receives
    /// page 1 again instead of an empty page, which makes an offset-paging integration loop forever.
    /// </summary>
    public static int Offset(int page, int pageSize)
        => (int)Math.Min((long)(Math.Max(1, page) - 1) * Math.Max(1, pageSize), int.MaxValue);
}
