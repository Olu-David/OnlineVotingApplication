using Microsoft.AspNetCore.Http;
using OnlineVotingApplication.Repository.iServices;
using System;

namespace OnlineVotingApplication.Repository.Services
{
    public class TenantProvider : ITenantProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private const string TenantSessionKey = "ActiveTenantId";

        #region TenantProvider
        public TenantProvider(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }
        #endregion

        #region GetCurrentTenantId
        public Guid GetCurrentTenantId()
        {
            var context = _httpContextAccessor.HttpContext;
            if (context == null) return Guid.Empty;

            // 1. Check if the Tenant ID exists in Session
            var sessionTenantId = context.Session.GetString(TenantSessionKey);
            if (!string.IsNullOrEmpty(sessionTenantId) && Guid.TryParse(sessionTenantId, out var sessionGuid))
            {
                return sessionGuid;
            }

            // 2. Fallback: Check query string (?tenantId=...)
            if (context.Request.Query.TryGetValue("tenantId", out var queryTenantId))
            {
                if (Guid.TryParse(queryTenantId, out var queryGuid))
                {
                    context.Session.SetString(TenantSessionKey, queryGuid.ToString());
                    return queryGuid;
                }
            }

            return Guid.Empty;
        }
        #endregion

        #region SetTenantContext
        public void SetTenantContext(Guid tenantId)
        {
            var context = _httpContextAccessor.HttpContext;
            if (context?.Session != null)
            {
                context.Session.SetString(TenantSessionKey, tenantId.ToString());
            }
        }
        #endregion

        #region ClearTenantContext
        public void ClearTenantContext()
        {
            var context = _httpContextAccessor.HttpContext;
            if (context?.Session != null)
            {
                context.Session.Remove(TenantSessionKey);
            }
        }
        #endregion
    }
}