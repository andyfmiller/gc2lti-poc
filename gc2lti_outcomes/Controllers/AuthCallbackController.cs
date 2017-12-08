using Google.Apis.Auth.OAuth2.AspMvcCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using AppFlowMetadata = gc2lti_outcomes.Data.AppFlowMetadata;
using Gc2LtiDbContext = gc2lti_outcomes.Data.Gc2LtiDbContext;

namespace gc2lti_outcomes.Controllers
{
    [Route("[controller]")]
    public class AuthCallbackController : Google.Apis.Auth.OAuth2.AspMvcCore.Controllers.AuthCallbackController
    {
        private readonly IConfiguration _configuration;
        private readonly Gc2LtiDbContext _context;

        public AuthCallbackController(IConfiguration config, Gc2LtiDbContext context)
        {
            _configuration = config;
            _context = context;
        }

        protected override FlowMetadata FlowData
        {
            get
            {
                var clientId = _configuration["Authentication:Google:ClientId"];
                var clientSecret = _configuration["Authentication:Google:ClientSecret"];

                return new AppFlowMetadata(clientId, clientSecret, _context);
            }
        }
    }
}