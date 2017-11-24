# gc2lti-poc
The is a proof of concept for using LTI Tools as assignments in Google Classroom.

There are two web applications in this solution: [catalog](https://github.com/andyfmiller/gc2lti-poc/tree/master/catalog) 
and [gc2lti](https://github.com/andyfmiller/gc2lti-poc/tree/master/gc2lti).

[catalog](https://github.com/andyfmiller/gc2lti-poc/tree/master/catalog) has a simulated catalog-style 
[page](https://github.com/andyfmiller/gc2lti-poc/blob/master/catalog/Pages/Resource.cshtml) for an LTI 
Tool that you want to use as an assignment in Google Classroom. There is a Google Classroom share button
on the page which shares a link to [gc2lti](https://github.com/andyfmiller/gc2lti-poc/tree/master/gc2lti).

[gc2lti](https://github.com/andyfmiller/gc2lti-poc/tree/master/gc2lti) has a 
[controller](https://github.com/andyfmiller/gc2lti-poc/blob/master/gc2lti/Controllers/Gc2LtiController.cs)
which uses the [Google Classroom API](https://developers.google.com/classroom/) and 
[Google Directory API](https://developers.google.com/admin-sdk/directory/) to form a complete and valid LTI request.
Then that request is signed and posted to the LTI Tool.

Both web applications use .NET Core 2.0. Unfortunately, [Google.Apis.Auth.Mvc](https://www.nuget.org/packages/Google.Apis.Auth.Mvc/)
is [not compatible](https://github.com/google/google-api-dotnet-client/issues/933) with .NET Core 2.0 applications, so I used 
[@buzallen](https://github.com/buzallen/google-api-dotnet-client/tree/master/Src/Support/Google.Apis.Auth.AspMvcCore)'s 
replacement implementation.