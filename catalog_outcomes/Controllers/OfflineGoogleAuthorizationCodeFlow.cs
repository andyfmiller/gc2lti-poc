//using System;
//using Google.Apis.Auth.OAuth2.Flows;
//using Google.Apis.Auth.OAuth2.Requests;

//namespace catalog_outcomes.Controllers
//{
//    public class OfflineGoogleAuthorizationCodeFlow : GoogleAuthorizationCodeFlow
//    {
//        public OfflineGoogleAuthorizationCodeFlow(Initializer initializer) : base(initializer)
//        {
//        }

//        public override AuthorizationCodeRequestUrl CreateAuthorizationCodeRequest(string redirectUri)
//        {
//            // Force offline authorization to generate refreshToken
//            return new GoogleAuthorizationCodeRequestUrl(new Uri(AuthorizationServerUrl))
//            {
//                ClientId = ClientSecrets.ClientId,
//                Scope = string.Join(" ", Scopes),
//                RedirectUri = redirectUri,
//                AccessType = "offline",
//                ApprovalPrompt = "force"
//            };
//        }
//    }
//}
