namespace OnlineVotingApplication.DataTransferView
{
    public class PaginatedListViewModel<T>
    {
            public IEnumerable<T> Items { get; set; } = Enumerable.Empty<T>();
            public int PageNumber { get; set; }
            public int PageSize { get; set; }
            public int TotalItems { get; set; }
            public int TotalPages => (int)Math.Ceiling((double)TotalItems / PageSize);

            public bool HasPreviousPage => PageNumber > 1;
            public bool HasNextPage => PageNumber < TotalPages;

        public bool NextPage { get;  set; }
        public int TotalCount { get; internal set; }
    }
}

