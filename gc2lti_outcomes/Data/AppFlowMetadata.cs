using System;
using Google.Apis.Admin.Directory.directory_v1;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.AspMvcCore;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Classroom.v1;
using Microsoft.AspNetCore.Mvc;

namespace gc2lti_outcomes.Data
{
    public class AppFlowMetadata : FlowMetadata
    {
        public AppFlowMetadata(string clientId, string secret, Gc2LtiDbContext context)
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
                        //=========================================================================
                        // HomeController creates the assignment.
                        // Gc2LtiController launches the assignment (the LTI tool).
                        // OutcomesController saves the grade.
                        //=========================================================================

                        // https://developers.google.com/classroom/reference/rest/v1/courses/list
                        // While sharing to classroom, get the list of the current user's courses
                        // so they can choose which one gets the assignment.
                        // https://developers.google.com/classroom/reference/rest/v1/courses/get
                        // While sharing to classroom, get the course so it's name can be displayed
                        // in the share to classroom confirmation, and so a View button can be
                        // constructed that displays the course with the assignment. And while
                        // launching the assignment, get the course so it's id and name can be
                        // passed to the LTI tool.
                        ClassroomService.Scope.ClassroomCoursesReadonly,

                        // https://developers.google.com/classroom/reference/rest/v1/userProfiles/get
                        // While sharing to classroom, get the current user's profile so their name 
                        // can be displayed in the share to classroom dialog (as a reminder of which
                        // account is signed on). And while launching the assignment, get the user's
                        // profile so their id, email, names, and photo can be passed to the LTI tool.
                        // https://developers.google.com/classroom/reference/rest/v1/courses.teachers/get
                        // While launching the assignment, find out if the current user is one of the
                        // teachers of the course so the role can be passed to the LTI tool.
                        ClassroomService.Scope.ClassroomRostersReadonly,
                        ClassroomService.Scope.ClassroomProfileEmails,
                        ClassroomService.Scope.ClassroomProfilePhotos,

                        // https://developers.google.com/classroom/reference/rest/v1/courses.courseWork/create
                        // While sharing to classroom, create the assignment.
                        // https://developers.google.com/classroom/reference/rest/v1/courses.courseWork.studentSubmissions/patch
                        // While saving a grade, patch the studentsubmission.
                        ClassroomService.Scope.ClassroomCourseworkStudents,

                        // https://developers.google.com/classroom/reference/rest/v1/courses.courseWork/get
                        // While saving a grade, get the coursework (assignment).
                        // https://developers.google.com/classroom/reference/rest/v1/courses.courseWork.studentSubmissions/list
                        // While saving a grade, get the studentsubmission for the assignment.
                        ClassroomService.Scope.ClassroomCourseworkStudentsReadonly,

                        // https://developers.google.com/classroom/reference/rest/v1/courses.courseWork/list
                        // While launching the assignment, get the corresponding coursework id, title, and
                        // description so it can be passed to the LTI tool.
                        ClassroomService.Scope.ClassroomCourseworkMeReadonly,

                        // https://developers.google.com/admin-sdk/directory/v1/reference/users/get
                        // While launching the assignment, get the current user's directory entry so
                        // their SIS ID and organization (school) can be passed to the LTI tool.
                        DirectoryService.Scope.AdminDirectoryUserReadonly,
                    },
                    DataStore = new EfDataStore(context)
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
