using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using gc2lti_outcomes.Data;
using gc2lti_outcomes.Models;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.AspMvcCore;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Auth.OAuth2.Web;
using Google.Apis.Classroom.v1;
using Google.Apis.Classroom.v1.Data;
using Google.Apis.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Configuration;

namespace gc2lti_outcomes.Controllers
{
    public class HomeController : Controller
    {
        public HomeController(IConfiguration config, Gc2LtiDbContext db)
        {
            Configuration = config;
            Db = db;
        }

        private IConfiguration Configuration { get; }
        private Gc2LtiDbContext Db { get; }

        private string ClientId
        {
            get { return Configuration["Authentication:Google:ClientId"]; }
        }

        private string ClientSecret
        {
            get { return Configuration["Authentication:Google:ClientSecret"]; }
        }

        /// <summary>
        /// This is the Home Page. It displays the "catalog" (one resource).
        /// </summary>
        /// <returns></returns>
        public IActionResult Index()
        {
            var model = new ResourceModel
            {
                Title = "Counting Raindrops",
                Description = "This is a lesson on estimation.",
                Url = "https://lti.tools/test/tp.php"
            };
            return View(model);
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        /// <summary>
        /// 1. Collect the courseId
        /// </summary>
        public async Task<IActionResult> Course(CancellationToken cancellationToken, CourseSelectionModel model)
        {
            var result = await new AuthorizationCodeMvcApp(this, new AppFlowMetadata(ClientId, ClientSecret, Db))
                .AuthorizeAsync(cancellationToken)
                .ConfigureAwait(false);

            if (result.Credential == null)
            {
                return Redirect(result.RedirectUri);
            }

            try
            {
                using (var classroomService = new ClassroomService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = result.Credential,
                    ApplicationName = "gc2lti"
                }))
                {
                    if (!await SaveOfflineToken(cancellationToken, classroomService, result))
                    {
                        return RedirectToAction("Course", model);
                    }

                    // Get the user's name
                    var profileRequest = classroomService.UserProfiles.Get("me");
                    var profile = await profileRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                    model.PersonName = profile.Name.FullName;

                    // Get the list of the user's courses
                    var coursesRequest = classroomService.Courses.List();
                    coursesRequest.CourseStates = CoursesResource.ListRequest.CourseStatesEnum.ACTIVE;
                    coursesRequest.TeacherId = "me";
                    ListCoursesResponse coursesResponse = null;
                    var courses = new List<SelectListItem>();
                    do
                    {
                        if (coursesResponse != null)
                        {
                            coursesRequest.PageToken = coursesResponse.NextPageToken;
                        }

                        coursesResponse =
                            await coursesRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                        if (coursesResponse.Courses != null)
                        {
                            courses.AddRange
                            (
                                coursesResponse.Courses.Select(c => new SelectListItem
                                {
                                    Value = c.Id,
                                    Text = c.Name
                                })
                            );
                        }
                    } while (!string.IsNullOrEmpty(coursesResponse.NextPageToken));

                    model.Courses = new SelectList(courses, "Value", "Text");

                    return View(model);
                }
            }
            catch (GoogleApiException e) when (e.Message.Contains("invalid authentication credentials"))
            {
                // Force a new UserId
                TempData.Remove("user");
                return RedirectToAction("Course", model);
            }
            catch (TokenResponseException e) when (e.Message.Contains("invalid_grant"))
            {
                // Force a new UserId
                TempData.Remove("user");
                return RedirectToAction("Course", model);
            }
            catch (Exception e)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }

        /// <summary>
        /// 2. Confirm the title, description, tool url, and max points
        /// </summary>
        public async Task<IActionResult> Confirm(CancellationToken cancellationToken, CourseWorkModel model)
        {
            var appFlow = new AppFlowMetadata(ClientId, ClientSecret, Db);
            var token = await appFlow.Flow.LoadTokenAsync(appFlow.GetUserId(this), cancellationToken);
            var credential = new UserCredential(appFlow.Flow, appFlow.GetUserId(this), token);

            try
            {
                // Get the course name
                using (var classroomService = new ClassroomService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential, //result.Credential,
                    ApplicationName = "gc2lti"
                }))
                {
                    var request = classroomService.Courses.Get(model.CourseId);
                    var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);

                    model.CourseName = response.Name;
                    model.MaxPoints = 100;

                    return View(model);
                }
            }
            catch (Exception e)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }

        /// <summary>
        /// 3. Create the assignment
        /// </summary>
        public async Task<IActionResult> Assign(CancellationToken cancellationToken, CourseWorkModel model)
        {
            var appFlow = new AppFlowMetadata(ClientId, ClientSecret, Db);
            var token = await appFlow.Flow.LoadTokenAsync(appFlow.GetUserId(this), cancellationToken);
            var credential = new UserCredential(appFlow.Flow, appFlow.GetUserId(this), token);

            try
            {
                // Create the assignment
                using (var classroomService = new ClassroomService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "gc2lti"
                }))
                {
                    var nonce = CalculateNonce(8);
                    var linkUrl = IsRequestLocal()
                            ? $"{Configuration["Localhost"]}/gc2lti/{nonce}?url={model.Url}&c={model.CourseId}"
                            : $"{Configuration["Remotehost"]}/gc2lti/{nonce}?url={model.Url}&c={model.CourseId}";
                    var courseWork = new CourseWork
                    {
                        Title = model.Title,
                        Description = model.Description,
                        MaxPoints = model.MaxPoints,
                        WorkType = "ASSIGNMENT",
                        Materials = new List<Material>
                        {
                            new Material
                            {
                                Link = new Link
                                {
                                    Title = model.Title,
                                    Url = linkUrl
                                }
                            }
                        },
                        State = "PUBLISHED"
                    };

                    var createRequest = classroomService.Courses.CourseWork.Create(courseWork, model.CourseId);
                    courseWork = await createRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);

                    model.CourseWorkId = courseWork.Id;

                    return RedirectToAction("View", model);
                }
            }
            catch (Exception e)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }

        /// <summary>
        /// 4. Display results with link to view the course
        /// </summary>
        /// <returns></returns>
        public async Task<IActionResult> View(CancellationToken cancellationToken, CourseWorkModel model)
        {
            var appFlow = new AppFlowMetadata(ClientId, ClientSecret, Db);
            var token = await appFlow.Flow.LoadTokenAsync(appFlow.GetUserId(this), cancellationToken);
            var credential = new UserCredential(appFlow.Flow, appFlow.GetUserId(this), token);

            try
            {
                // Get information using Google Classroom API
                using (var classroomService = new ClassroomService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "gc2lti"
                }))
                {
                    var request = classroomService.Courses.Get(model.CourseId);
                    var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);

                    model.CourseAlternativeLink = response.AlternateLink;

                    return View(model);
                }
            }
            catch (Exception e)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }

        /// <summary>
        /// If the Google User was prompted to agree to the permissions, then
        /// the TokenResponse will include a RefreshToken which is suitable for
        /// offline use. This is the TokenResponse to use for sending grades back.
        /// Keep track of the UserId so we can look up the TokenResponse later.
        /// </summary>
        private async Task<bool> SaveOfflineToken(CancellationToken cancellationToken, ClassroomService classroomService, AuthorizationCodeWebApp.AuthResult result)
        {
            var userProfileRequest = classroomService.UserProfiles.Get("me");
            var userProfile =
                await userProfileRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            var googleUser = await Db.GoogleUsers
                .FindAsync(new object[] { userProfile.Id }, cancellationToken).ConfigureAwait(false);

            // If there is no matching GoogleUser, then we need to make one
            if (googleUser == null)
            {
                // If we don't have an offline Token, force a login and acceptance
                if (string.IsNullOrEmpty(result.Credential.Token.RefreshToken))
                {
                    await result.Credential.RevokeTokenAsync(cancellationToken).ConfigureAwait(false);
                    return false;
                }

                // Otherwise record a reference to the token
                googleUser = new GoogleUser
                {
                    GoogleId = userProfile.Id,
                    UserId = result.Credential.UserId
                };
                await Db.AddAsync(googleUser, cancellationToken).ConfigureAwait(false);
                await Db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            // If there is a matching GoogleUser with a new offline Token, record it
            else if (!googleUser.UserId.Equals(result.Credential.UserId) 
                && result.Credential.Token.RefreshToken != null)
            {
                googleUser.UserId = result.Credential.UserId;
                Db.Update(googleUser);
                await Db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            return true;
        }

        private bool IsRequestLocal()
        {
            return Request.HttpContext.Connection.RemoteIpAddress == null
                   || Request.HttpContext.Connection.LocalIpAddress == null
                   || Request.HttpContext.Connection.RemoteIpAddress.Equals(Request.HttpContext.Connection.LocalIpAddress)
                   || IPAddress.IsLoopback(Request.HttpContext.Connection.RemoteIpAddress);
        }

        // Return a nonce to differentiate assignments with the same URL within a course
        private static string CalculateNonce(int length)
        {
            const string allowableCharacters = "abcdefghijklmnopqrstuvwxyz0123456789";
            var bytes = new byte[length];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }
            return new string(bytes.Select(x => allowableCharacters[x % allowableCharacters.Length]).ToArray());
        }

    }
}
