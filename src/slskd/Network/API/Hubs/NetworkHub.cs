﻿// <copyright file="NetworkHub.cs" company="slskd Team">
//     Copyright (c) slskd Team. All rights reserved.
//
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published
//     by the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
//
//     You should have received a copy of the GNU Affero General Public License
//     along with this program.  If not, see https://www.gnu.org/licenses/.
// </copyright>

namespace slskd.Network
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.SignalR;
    using Serilog;

    public static class NetworkHubMethods
    {
        public static readonly string RequestFile = "REQUEST_FILE";
        public static readonly string RequestFileInfo = "REQUEST_FILE_INFO";
        public static readonly string AuthenticationChallenge = "AUTHENTICATION_CHALLENGE";
        public static readonly string AuthenticationChallengeAccepted = "AUTHENTICATION_CHALLENGE_ACCEPTED";
        public static readonly string AuthenticationChallengeRejected = "AUTHENTICATION_CHALLENGE_REJECTED";
    }

    /// <summary>
    ///     Extension methods for the network SignalR hub.
    /// </summary>
    public static class NetworkHubExtensions
    {
        public static Task RequestFileAsync(this IHubContext<NetworkHub> hub, string agent, string filename, Guid id)
        {
            // todo: how to send this to the specified agent?
            return hub.Clients.All.SendAsync(NetworkHubMethods.RequestFile, filename, id);
        }

        public static Task RequestFileInfoAsync(this IHubContext<NetworkHub> hub, string agent, string filename, Guid id)
        {
            return hub.Clients.All.SendAsync(NetworkHubMethods.RequestFileInfo, filename, id);
        }
    }

    /// <summary>
    ///     The network SignalR hub.
    /// </summary>
    [Authorize]
    public class NetworkHub : Hub
    {
        public NetworkHub(
            INetworkService networkService)
        {
            Network = networkService;
        }

        private INetworkService Network { get; }
        private ILogger Log { get; } = Serilog.Log.ForContext<NetworkService>();

        public override async Task OnConnectedAsync()
        {
            Log.Information("Agent connection {Id} established. Sending authentication challenge...", Context.ConnectionId);
            await Clients.Caller.SendAsync(NetworkHubMethods.AuthenticationChallenge, Network.GenerateAuthenticationChallengeToken(Context.ConnectionId));
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            if (Network.TryRemoveAgentRegistration(Context.ConnectionId, out var agent))
            {
                Log.Warning("Agent {Agent} (connection {Id}) disconnected", agent, Context.ConnectionId);
            }

            return Task.CompletedTask;
        }

        public bool Login(string agent, string challengeResponse)
        {
            if (Network.TryValidateAuthenticationChallengeResponse(Context.ConnectionId, agent, challengeResponse))
            {
                Log.Information("Agent connection {Id} authenticated as agent {Agent}", Context.ConnectionId, agent);
                Network.RegisterAgent(Context.ConnectionId, agent);
                return true;
            }

            Log.Information("Agent connection {Id} authentication failed", Context.ConnectionId);
            Network.TryRemoveAgentRegistration(Context.ConnectionId, out var _); // just in case!
            return false;
        }

        public Guid GetShareUploadToken()
        {
            if (Network.TryGetAgentRegistration(Context.ConnectionId, out var agent))
            {
                return Network.GetShareUploadToken(agent);
            }

            // this can happen if the agent attempts to upload before logging in
            Log.Information("Agent connection {Id} requested a share upload token, but was not registered.", Context.ConnectionId);
            throw new UnauthorizedAccessException();
        }

        public void NotifyUploadFailed(Guid id)
        {
            if (Network.PendingFileUploads.TryGetValue(id, out var record))
            {
                record.Stream.TrySetException(new NotFoundException("The file could not be uploaded from the remote agent"));
            }
        }

        public void ReturnFileInfo(Guid id, bool exists, long length)
        {
            if (Network.PendingFileInquiries.TryGetValue(id, out var record))
            {
                record.TrySetResult((exists, length));
            }
        }
    }
}