# gc2lti-poc
The is a proof of concept for using LTI Tools as assignments in Google Classroom.

There are two web applications in this solution: [catalog](https://github.com/andyfmiller/gc2lti-poc/tree/master/catalog) 
and [gc2lti](https://github.com/andyfmiller/gc2lti-poc/tree/master/gc2lti).

[catalog](https://github.com/andyfmiller/gc2lti-poc/tree/master/catalog) has a simulated catalog-style 
[page](https://github.com/andyfmiller/gc2lti-poc/blob/master/catalog/Pages/Resource.cshtml) for an LTI 
Tool that you want to use as an assignment in Google Classroom. There is a Google 
[Classroom share button](https://developers.google.com/classroom/guides/sharebutton)
on the page which shares a link to [gc2lti](https://github.com/andyfmiller/gc2lti-poc/tree/master/gc2lti).

[gc2lti](https://github.com/andyfmiller/gc2lti-poc/tree/master/gc2lti) has a 
[controller](https://github.com/andyfmiller/gc2lti-poc/blob/master/gc2lti/Controllers/Gc2LtiController.cs)
which uses the [Google Classroom API](https://developers.google.com/classroom/) and 
[Google Directory API](https://developers.google.com/admin-sdk/directory/) to form a complete and valid LTI request.
Then that request is signed and posted to the LTI Tool.

[gc2lti_outcomes](https://github.com/andyfmiller/gc2lti-poc/tree/master/gc2lti_outcomes) is a single project that combines the catalog with gc2lti and adds support for sending LTI Outcomes back to Google Classroom. A version of this project is running on [Azure](http://gc2lti-outcomes.azurewebsites.net/).

All 3 web applications use .NET Core 2.0. Unfortunately, [Google.Apis.Auth.Mvc](https://www.nuget.org/packages/Google.Apis.Auth.Mvc/)
is [not compatible](https://github.com/google/google-api-dotnet-client/issues/933) with .NET Core 2.0 applications, so I used 
[@buzallen](https://github.com/buzallen/google-api-dotnet-client/tree/master/Src/Support/Google.Apis.Auth.AspMvcCore)'s 
replacement implementation.

Read more about this POC in [Using LTI Tools in Google Classroom](https://andyfmiller.com/2017/11/24/using-lti-tools-in-google-classroom/) and [Sending LTI Outcomes to Google Classroom](https://andyfmiller.com/2017/12/12/sending-lti-outcomes-to-google-classroom/).

## QuickStart

All 3 projects are compatible with Visual Studio Community 2017 and .NET Core 2.0. The [catalog](https://github.com/andyfmiller/gc2lti-poc/tree/master/catalog) 
project should run as-is. The [gc2lti](https://github.com/andyfmiller/gc2lti-poc/tree/master/gc2lti) and [gc2lti_outcomes](https://github.com/andyfmiller/gc2lti-poc/tree/master/gc2lti_outcomes) projects have several prerequisites:

* A Google account (for you as the developer of the project).
* A second Google account with Google Classroom enabled for testing. A G Suite for Education account is preferred, but this works with other account types with some degradation.
* Download [@buzallen](https://github.com/buzallen)‘s Google.Apis.Auth.Mvc replacement he calls [Google.Apis.Auth.AspMvcCore](https://github.com/buzallen/google-api-dotnet-client/tree/master/Src/Support/Google.Apis.Auth.AspMvcCore). You will probably need to fix the Google.Apis.Auth.AspMvcCore project reference in the gc2lti-poc solution so that it points where you downloaded the project.
* Enable the Classroom API and the Admin SDK using the [Google Developers Console](https://console.developers.google.com/). See Google’s [Classroom Quickstart](https://developers.google.com/classroom/quickstart/dotnet) for details.
* Create an OAuth Client ID for a web application, also using the [Google Developers Console](https://console.developers.google.com/).
* Add "https://localhost:44319/AuthCallback" as an authorized Redirect URL to the Client ID.
* Store the resulting Client ID and Secret for the Gc2LtiController using the [Secret Manager](https://docs.microsoft.com/en-us/aspnet/core/security/app-secrets?tabs=visual-studio):
  1. Right click on the **gc2lti** project and select **Manage User Secrets**.
  2. Store your Client ID and Secret in the secrets.json file:
  ```
  {
    "Authentication:Google:ClientId": "YOUR CLIENT ID",
    "Authentication:Google:ClientSecret": "YOUR SECRET"
  }
  ```
  3. Repeat for the **gc2lti_outcomes** project.

All 3 projects should run now.
