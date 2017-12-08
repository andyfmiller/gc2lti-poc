using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using gc2lti_outcomes.Data;
using gc2lti_outcomes.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Classroom.v1;
using Google.Apis.Services;
using LtiLibrary.AspNetCore.Extensions;
using LtiLibrary.AspNetCore.Outcomes.v1;
using LtiLibrary.NetCore.Lti.v1;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace gc2lti_outcomes.Controllers
{
    [Route("[controller]")]
    public class OutcomesController : OutcomesControllerBase
    {
        private IConfiguration Configuration { get; }
        private Gc2LtiDbContext Db { get; }

        public OutcomesController(IConfiguration config, Gc2LtiDbContext db)
        {
            Configuration = config;
            Db = db;
        }

        private string ClientId
        {
            get { return Configuration["Authentication:Google:ClientId"]; }
        }

        private string ClientSecret
        {
            get { return Configuration["Authentication:Google:ClientSecret"]; }
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

            // Google Classroom does not support deleting a grade

            response.StatusCode = StatusCodes.Status501NotImplemented;
            response.StatusDescription = "Google Classroom does not support deleting submissions.";

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

            // Read the grade from Google Classroom
            try
            {
                var lisResultSourcedId = JsonConvert.DeserializeObject<LisResultSourcedId>(arg.LisResultSourcedId);
                var googleUser = await Db.GoogleUsers.FindAsync(lisResultSourcedId.TeacherId);
                var appFlow = new AppFlowMetadata(ClientId, ClientSecret, Db);
                var token = await appFlow.Flow.LoadTokenAsync(googleUser.UserId, CancellationToken.None);
                var credential = new UserCredential(appFlow.Flow, googleUser.UserId, token);

                using (var classroomService = new ClassroomService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "gc2lti"
                }))
                {
                    var courseWorkRequest = classroomService.Courses.CourseWork.Get
                    (
                        lisResultSourcedId.CourseId,
                        lisResultSourcedId.CourseWorkId
                    );
                    var courseWork = await courseWorkRequest.ExecuteAsync();

                    var submissionsRequest = classroomService.Courses.CourseWork.StudentSubmissions.List
                    (
                        lisResultSourcedId.CourseId,
                        lisResultSourcedId.CourseWorkId
                    );
                    submissionsRequest.UserId = lisResultSourcedId.StudentId;
                    var submissionsResponse = await submissionsRequest.ExecuteAsync();
                    var submission = submissionsResponse.StudentSubmissions.FirstOrDefault();

                    if (submission == null)
                    {
                        response.StatusCode = StatusCodes.Status404NotFound;
                        response.StatusDescription = "Submission was found.";
                    }
                    else
                    {
                        response.Result = new Result
                        {
                            SourcedId = arg.LisResultSourcedId,
                            Score = submission.AssignedGrade / courseWork.MaxPoints
                        };
                        response.StatusDescription = $"Score={response.Result.Score}, AssignedGrade={submission.AssignedGrade}.";
                    }
                }
            }
            catch (Exception)
            {
                response.StatusCode = StatusCodes.Status500InternalServerError;
            }

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
            var lisResultSourcedId = JsonConvert.DeserializeObject<LisResultSourcedId>(arg.Result.SourcedId);
            var googleUser = await Db.GoogleUsers.FindAsync(lisResultSourcedId.TeacherId);
            var appFlow = new AppFlowMetadata(ClientId, ClientSecret, Db);
            var token = await appFlow.Flow.LoadTokenAsync(googleUser.UserId, CancellationToken.None);
            var credential = new UserCredential(appFlow.Flow, googleUser.UserId, token);

            using (var classroomService = new ClassroomService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "gc2lti"
            }))
            {
                var courseWorkRequest = classroomService.Courses.CourseWork.Get
                (
                    lisResultSourcedId.CourseId,
                    lisResultSourcedId.CourseWorkId
                );
                var courseWork = await courseWorkRequest.ExecuteAsync();

                var submissionsRequest = classroomService.Courses.CourseWork.StudentSubmissions.List
                (
                    lisResultSourcedId.CourseId,
                    lisResultSourcedId.CourseWorkId
                );
                submissionsRequest.UserId = lisResultSourcedId.StudentId;
                var submissionsResponse = await submissionsRequest.ExecuteAsync();
                if (submissionsResponse.StudentSubmissions == null)
                {
                    response.StatusCode = StatusCodes.Status404NotFound;
                    response.StatusDescription = "Submission was found.";
                    return response;
                }

                var submission = submissionsResponse.StudentSubmissions.FirstOrDefault();
                if (submission == null)
                {
                    response.StatusCode = StatusCodes.Status404NotFound;
                    response.StatusDescription = "Submission was found.";
                }
                else
                {
                    submission.AssignedGrade = arg.Result.Score * courseWork.MaxPoints;

                    var patchRequest = classroomService.Courses.CourseWork.StudentSubmissions.Patch
                    (
                        submission,
                        submission.CourseId,
                        submission.CourseWorkId,
                        submission.Id
                    );
                    patchRequest.UpdateMask = "AssignedGrade";
                    await patchRequest.ExecuteAsync();
                    response.StatusDescription = $"Score={arg.Result.Score}, AssignedGrade={submission.AssignedGrade}.";
                }
            }

            return response;
        }
    }
}