﻿using System;
using System.Web;
using Microsoft.Owin;
using Microsoft.Owin.Security;
using Umbraco.Core;
using Umbraco.Web.Models.Identity;
using Umbraco.Web.Security;

namespace Umbraco.Web
{
    public static class OwinExtensions
    {
        /// <summary>
        /// Gets the <see cref="ISecureDataFormat{AuthenticationTicket}"/> for the Umbraco back office cookie
        /// </summary>
        /// <param name="owinContext"></param>
        /// <returns></returns>
        internal static ISecureDataFormat<AuthenticationTicket> GetUmbracoAuthTicketDataProtector(this IOwinContext owinContext)
        {
            var found = owinContext.Get<UmbracoAuthTicketDataProtector>();
            return found?.Protector;
        }

        public static string GetCurrentRequestIpAddress(this IOwinContext owinContext)
        {
            if (owinContext == null)
            {
                return "Unknown, owinContext is null";
            }
            if (owinContext.Request == null)
            {
                return "Unknown, owinContext.Request is null";
            }

            var httpContext = owinContext.TryGetHttpContext();
            if (httpContext == false)
            {
                return "Unknown, cannot resolve HttpContext from owinContext";
            }

            return httpContext.Result.GetCurrentRequestIpAddress();
        }

        /// <summary>
        /// Nasty little hack to get HttpContextBase from an owin context
        /// </summary>
        /// <param name="owinContext"></param>
        /// <returns></returns>
        internal static Attempt<HttpContextBase> TryGetHttpContext(this IOwinContext owinContext)
        {
            var ctx = owinContext.Get<HttpContextBase>(typeof(HttpContextBase).FullName);
            return ctx == null ? Attempt<HttpContextBase>.Fail() : Attempt.Succeed(ctx);
        }
        
        /// <summary>
        /// Gets the back office sign in manager out of OWIN
        /// </summary>
        /// <param name="owinContext"></param>
        /// <returns></returns>
        public static BackOfficeSignInManager2 GetBackOfficeSignInManager2(this IOwinContext owinContext)
        {
            return owinContext.Get<BackOfficeSignInManager2>()
                ?? throw new NullReferenceException($"Could not resolve an instance of {typeof (BackOfficeSignInManager2)} from the {typeof(IOwinContext)}.");
        }

        /// <summary>
        /// Gets the back office user manager out of OWIN
        /// </summary>
        /// <param name="owinContext"></param>
        /// <returns></returns>
        /// <remarks>
        /// This is required because to extract the user manager we need to user a custom service since owin only deals in generics and
        /// developers could register their own user manager types
        /// </remarks>
        public static BackOfficeUserManager2<BackOfficeIdentityUser> GetBackOfficeUserManager2(this IOwinContext owinContext)
        {
            var marker = owinContext.Get<IBackOfficeUserManagerMarker2>(BackOfficeUserManager2.OwinMarkerKey)
                ?? throw new NullReferenceException($"No {typeof (IBackOfficeUserManagerMarker2)}, i.e. no Umbraco back-office, has been registered with Owin.");

            return marker.GetManager(owinContext)
                ?? throw new NullReferenceException($"Could not resolve an instance of {typeof (BackOfficeUserManager2<BackOfficeIdentityUser>)} from the {typeof (IOwinContext)}.");
        }

        /// <summary>
        /// Adapted from Microsoft.AspNet.Identity.Owin.OwinContextExtensions
        /// </summary>
        public static T Get<T>(this IOwinContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return context.Get<T>(GetKey(typeof(T)));
        }

        private static string GetKey(Type t)
        {
            return "AspNet.Identity.Owin:" + t.AssemblyQualifiedName;
        }
    }
}
