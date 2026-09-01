namespace OnlineVotingApplication.Repository.iServices
{
    public interface ITenantProvider
    {
        Guid GetCurrentTenantId();
        void SetTenantContext(Guid tenantId);
    }
}
