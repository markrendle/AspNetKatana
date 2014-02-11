﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.Owin.Logging;
using Microsoft.Owin.Security.Infrastructure;

namespace Microsoft.Owin.Security.WsFederation
{
    public class WsFederationAuthenticationHandler : AuthenticationHandler<WsFederationAuthenticationOptions>
    {
        [SuppressMessage("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields", Justification = "Will be used soon.")]
        private readonly ILogger _logger;

        public WsFederationAuthenticationHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Handles Signout
        /// </summary>
        /// <returns></returns>
        protected override async Task ApplyResponseGrantAsync()
        {
            AuthenticationResponseRevoke signout = Helper.LookupSignOut(Options.AuthenticationType, Options.AuthenticationMode);
            if (signout != null)
            {
                object obj = null;
                Context.Environment.TryGetValue(WsFederationParameterNames.Wreply, out obj);
                string wreply = obj as string;

                WsFederationMessage wsFederationMessage = new WsFederationMessage()
                {
                    IssuerAddress = Options.IssuerAddress ?? string.Empty,
                    Wreply = wreply ?? Options.Wreply,
                    Wtrealm = Options.Wtrealm,
                };

                if (Options.Notifications != null && Options.Notifications.RedirectToIdentityProvider != null)
                {
                    RedirectToIdentityProviderNotification<WsFederationMessage> notification = new RedirectToIdentityProviderNotification<WsFederationMessage> { ProtocolMessage = wsFederationMessage };
                    await Options.Notifications.RedirectToIdentityProvider(notification);
                    if (notification.Cancel)
                    {
                        return;
                    }
                }

                string redirect = wsFederationMessage.CreateSignOutQueryString();
                if (!Uri.IsWellFormedUriString(redirect, UriKind.Absolute))
                {
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, Resources.Exception_InvalidSignOutUri, redirect));
                }

                Response.Redirect(redirect);
            }
        }

        protected override Task ApplyResponseChallengeAsync()
        {
            if (Response.StatusCode == 401)
            {
                // TODO [brentsch] - need to study this matching.

                string baseUri =
                        Request.Scheme +
                        Uri.SchemeDelimiter +
                        Request.Host +
                        Request.PathBase;

                string currentUri =
                    baseUri +
                    Request.Path +
                    Request.QueryString;

                AuthenticationResponseChallenge challenge = Helper.LookupChallenge(Options.AuthenticationType, Options.AuthenticationMode);
                if (challenge == null)
                {
                    return Task.FromResult<object>(null);
                }

                // Add CSRF correlation id to the states
                AuthenticationProperties properties = challenge.Properties;
                if (string.IsNullOrEmpty(properties.RedirectUri))
                {
                    properties.RedirectUri = currentUri;
                }

                GenerateCorrelationId(properties);
                WsFederationMessage wsFederationMessage = new WsFederationMessage()
                {
                    IssuerAddress = Options.IssuerAddress ?? string.Empty,
                    Wreply = currentUri,
                    Wtrealm = Options.Wtrealm,
                };

                if (Options.Notifications != null && Options.Notifications.RedirectToIdentityProvider != null)
                {
                    RedirectToIdentityProviderNotification<WsFederationMessage> notification = new RedirectToIdentityProviderNotification<WsFederationMessage> { ProtocolMessage = wsFederationMessage };
                    Options.Notifications.RedirectToIdentityProvider(notification);
                    if (notification.Cancel)
                    {
                        return Task.FromResult<object>(null);
                    }
                }

                string redirect = wsFederationMessage.CreateSignInQueryString();
                if (!Uri.IsWellFormedUriString(redirect, UriKind.Absolute))
                {
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, Resources.Exception_InvalidSignInUri, redirect));
                }

                Response.Redirect(redirect);
                return Task.FromResult<object>(null);
            }

            return Task.FromResult<object>(null);
        }

        protected override async Task<AuthenticationTicket> AuthenticateCoreAsync()
        {
            // TODO: Ship blocker: We can't read the body on a arbitrary request. We need to confirm that this a sign-in message.
            // Normally we confirm this by path or query value. What does ADFS do? How about AAD?
            IFormCollection form = await Request.ReadFormAsync();
            if (form == null)
            {
                return null;
            }

            // TODO: a delegate on WsFederationAuthenticationOptions would allow for users to hook there own custom processing of message.
            WsFederationMessage wsFederationMessage = new WsFederationMessage(form);
            if (wsFederationMessage.IsSignInMessage)
            {
                MessageReceivedNotification<WsFederationMessage> messageReceivedNotification = null;
                if (Options.Notifications != null && Options.Notifications.MessageReceived != null)
                {
                    messageReceivedNotification = new MessageReceivedNotification<WsFederationMessage> { ProtocolMessage = wsFederationMessage };
                    await Options.Notifications.MessageReceived(messageReceivedNotification);
                }

                if (messageReceivedNotification != null && messageReceivedNotification.Cancel)
                {
                    return null;
                }

                if (wsFederationMessage.Wresult != null)
                {
                    string token = wsFederationMessage.GetToken();
                    if (Options.Notifications != null && Options.Notifications.SecurityTokenReceived != null)
                    {
                        SecurityTokenReceivedNotification securityTokenReceivedNotification = new SecurityTokenReceivedNotification { SecurityToken = token };
                        await Options.Notifications.SecurityTokenReceived(securityTokenReceivedNotification);
                        if (securityTokenReceivedNotification.Cancel)
                        {
                            return null;
                        }
                    }

                    // TODO: if this fails because the key is not found, refresh the metadata.
                    // TODO: Error handling for validation failures.
                    ClaimsPrincipal principal = Options.SecurityTokenHandlers.ValidateToken(token, Options.TokenValidationParameters);
                    ClaimsIdentity claimsIdentity = principal.Identity as ClaimsIdentity;
                    AuthenticationTicket ticket = new AuthenticationTicket(principal.Identity as ClaimsIdentity, new AuthenticationProperties());
                    // TODO: Change to SignIn(ClaimsIdenity)
                    Request.Context.Authentication.AuthenticationResponseGrant = new AuthenticationResponseGrant(claimsIdentity, new AuthenticationProperties());
                    if (Options.Notifications != null && Options.Notifications.SecurityTokenValidated != null)
                    {
                        await Options.Notifications.SecurityTokenValidated(new SecurityTokenValidatedNotification { AuthenticationTicket = ticket });
                    }

                    return ticket;
                }
            }

            return null;
        }
    }
}