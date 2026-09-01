using OnlineVotingApplication.Repository.iServices;

namespace OnlineVotingApplication.Repository.Services
{
  
        public class TenantProvider : ITenantProvider
        {
            private readonly IHttpContextAccessor _httpContextAccessor;

            public TenantProvider(IHttpContextAccessor httpContextAccessor)
            {
                _httpContextAccessor = httpContextAccessor;
            }

            public Guid GetCurrentTenantId()
            {
                var context = _httpContextAccessor.HttpContext;
                if (context == null) return Guid.Empty;

                // 1. Check if the Tenant has been written to the encrypted session memory cookie
                var sessionTenantId = context.Session.GetString("ActiveTenantId");
                if (!string.IsNullOrEmpty(sessionTenantId) && Guid.TryParse(sessionTenantId, out var sessionGuid))
                {
                    return sessionGuid;
                }

                // 2. Fallback: Check if the tenant ID was passed directly inside the URL query parameters (?tenantId=...)
                if (context.Request.Query.TryGetValue("tenantId", out var queryTenantId))
                {
                    if (Guid.TryParse(queryTenantId, out var queryGuid))
                    {
                        // Lock this tenant choice into session cookie cache memory for subsequent clicks
                        context.Session.SetString("ActiveTenantId", queryGuid.ToString());
                        return queryGuid;
                    }
                }

                return Guid.Empty;
            }

            public void SetTenantContext(Guid tenantId)
            {
                var context = _httpContextAccessor.HttpContext;
                context?.Session.SetString("ActiveTenantId", tenantId.ToString());
            }


        }
    }



