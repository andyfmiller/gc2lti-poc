using System;
using System.Threading.Tasks;
using LtiLibrary.AspNetCore.Extensions;
using LtiLibrary.AspNetCore.Outcomes.v1;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace gc2lti_outcomes.Controllers
{
    [Route("[controller]")]
    public class OutcomesController : OutcomesControllerBase
    {
        public IConfiguration Configuration { get; set; }

        public OutcomesController(IConfiguration config)
        {
            Configuration = config;
        }

        protected override Func<DeleteResultRequest, Task<DeleteResultResponse>> OnDeleteResultAsync 
            => DeleteResultAsync;

        protected override Func<ReadResultRequest, Task<ReadResultResponse>> OnReadResultAsync
            => ReadResultAsync;

        protected override Func<ReplaceResultRequest, Task<ReplaceResultResponse>> OnReplaceResultAsync
            => ReplaceResultAsync;

        private async Task<DeleteResultResponse> DeleteResultAsync(DeleteResultRequest arg)
        {
            var response = new DeleteResultResponse();

            var ltiRequest = await Request.ParseLtiRequestAsync();
            var signature = ltiRequest.GenerateSignature("secret");
            if (!ltiRequest.Signature.Equals(signature))
            {
                response.StatusCode = StatusCodes.Status401Unauthorized;
                return response;
            }

            // Delete the grade in Google Classroom

            return response;
        }

        private async Task<ReadResultResponse> ReadResultAsync(ReadResultRequest arg)
        {
            var response = new ReadResultResponse();

            var ltiRequest = await Request.ParseLtiRequestAsync();
            var signature = ltiRequest.GenerateSignature("secret");
            if (!ltiRequest.Signature.Equals(signature))
            {
                response.StatusCode = StatusCodes.Status401Unauthorized;
                return response;
            }

            // Read the grade in Google Classroom

            return response;
        }

        private async Task<ReplaceResultResponse> ReplaceResultAsync(ReplaceResultRequest arg)
        {
            var response = new ReplaceResultResponse();

            var ltiRequest = await Request.ParseLtiRequestAsync();
            var signature = ltiRequest.GenerateSignature("secret");
            if (!ltiRequest.Signature.Equals(signature))
            {
                response.StatusCode = StatusCodes.Status401Unauthorized;
                return response;
            }

            // Record the grade in Google Classroom

            return response;
        }
    }
}