using System;
using Google.Apis.Admin.Directory.directory_v1;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.AspMvcCore;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Classroom.v1;
using Google.Apis.Util.Store;
using Microsoft.AspNetCore.Mvc;

namespace gc2lti.Controllers
{
    public class AppFlowMetadata : FlowMetadata
    {
        public AppFlowMetadata(string clientId, string secret)
        {
            Flow =
                new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = clientId,
                        ClientSecret = secret
                    },
                    Scopes = new[]
                    {
                        ClassroomService.Scope.ClassroomCoursesReadonly,
                        ClassroomService.Scope.ClassroomProfileEmails,
                        ClassroomService.Scope.ClassroomProfilePhotos,
                        ClassroomService.Scope.ClassroomRostersReadonly,
                        ClassroomService.Scope.ClassroomCourseworkMeReadonly,
                        ClassroomService.Scope.ClassroomCourseworkStudentsReadonly,
                        DirectoryService.Scope.AdminDirectoryUserReadonly
                    },
                    DataStore = new FileDataStore("Classroom.Api.Auth.Store")
                });
        }

        public override string GetUserId(Controller controller)
        {
            // In this sample we use the session to store the user identifiers.
            // That's not the best practice, because you should have a logic to identify
            // a user. You might want to use "OpenID Connect".
            // You can read more about the protocol in the following link:
            // https://developers.google.com/accounts/docs/OAuth2Login.

            var user = controller.TempData["user"];
            if (user == null)
            {
                user = Guid.NewGuid();
                controller.TempData["user"] = user;
            }
            return user.ToString();
        }

        public override string AuthCallback
        {
            // This must match a Redirect URI for the Client ID you are using
            get { return @"/AuthCallback"; }
        }

        public override IAuthorizationCodeFlow Flow { get; }
    }
}
