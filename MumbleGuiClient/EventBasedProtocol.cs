﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using MumbleSharp;
using MumbleSharp.Model;

namespace MumbleGuiClient
{
    /// <summary>
    /// An event based protocol for the GUI thing. Pops events when something happens. :-)
    /// </summary>
    public class EventBasedProtocol
        : IMumbleProtocol
    {
        MumbleConnection _connection;

        readonly ConcurrentDictionary<UInt32, User> _users = new ConcurrentDictionary<UInt32, User>();
        public IEnumerable<User> Users
        {
            get { return _users.Values; }
        }

        readonly ConcurrentDictionary<UInt32, Channel> _channels = new ConcurrentDictionary<UInt32, Channel>();
        public IEnumerable<Channel> Channels
        {
            get { return _channels.Values; }
        }

        public User LocalUser { get; private set; }
        public Channel RootChannel { get; private set; }
        /// <summary>
        /// Occurs when a text message is recieved, and returns it as a message class.
        /// </summary>
        public event EventHandler<Message> MessageRecieved;

        public void Initialise(MumbleConnection connection)
        {
            _connection = connection;
        }

        public void Version(MumbleSharp.Packets.Version version)
        {
        }

        public bool ValidateCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors)
        {
            return true;
        }

        #region Channels
        public void ChannelState(MumbleSharp.Packets.ChannelState channelState)
        {
            var channel = _channels.AddOrUpdate(channelState.ChannelId, i => new Channel(channelState.ChannelId, channelState.Name, channelState.Parent) { Temporary = channelState.Temporary },
                (i, c) =>
                {
                    c.Name = channelState.Name;
                    return c;
                }
            );

            if (channel.Id == 0)
                RootChannel = channel;
        }

        public void ChannelRemove(MumbleSharp.Packets.ChannelRemove channelRemove)
        {
            Channel c;
            _channels.TryRemove(channelRemove.ChannelId, out c);
        }
        #endregion

        #region users
        public void UserState(MumbleSharp.Packets.UserState userState)
        {
            if (userState.Session.HasValue)
            {
                User user = _users.AddOrUpdate(userState.Session.Value, i => new User(userState.Session.Value), (i, u) => u);

                if (userState.SelfDeaf.HasValue)
                    user.Deaf = userState.SelfDeaf.Value;
                if (userState.SelfMute.HasValue)
                    user.Muted = userState.SelfMute.Value;
                if (userState.Mute.HasValue)
                    user.Muted = userState.Mute.Value;
                if (userState.Deaf.HasValue)
                    user.Deaf = userState.Deaf.Value;
                if (userState.Suppress.HasValue)
                    user.Muted = userState.Suppress.Value;
                if (userState.Name != null)
                    user.Name = userState.Name;

                if (userState.ChannelId.HasValue)
                    user.Channel = _channels[userState.ChannelId.Value];
                else user.Channel = RootChannel;
            }
        }

        public void UserRemove(MumbleSharp.Packets.UserRemove userRemove)
        {
            User user;
            if (_users.TryRemove(userRemove.Session, out user))
                user.Dispose();
            if (user.Equals(LocalUser))
            {
                //Console.WriteLine(((userRemove.Ban) ? "Banned" : "Kicked") + " from server. Reason: " + userRemove.Reason);
                _connection.Close();
            }
        }
        #endregion

        public void ContextAction(MumbleSharp.Packets.ContextAction contextAction)
        {
        }

        public void ContextActionAdd(MumbleSharp.Packets.ContextActionAdd contextActionAdd)
        {
        }

        public void PermissionQuery(MumbleSharp.Packets.PermissionQuery permissionQuery)
        {
        }

        #region server setup
        public void ServerSync(MumbleSharp.Packets.ServerSync serverSync)
        {
            if (LocalUser != null)
                throw new InvalidOperationException("Second ServerSync Received");

            LocalUser = _users[serverSync.Session];
        }

        public void ServerConfig(MumbleSharp.Packets.ServerConfig serverConfig)
        {
        }
        #endregion

        #region voice
        public void CodecVersion(MumbleSharp.Packets.CodecVersion codecVersion)
        {
        }

        public void UdpPing(byte[] packet)
        {
        }

        public void Voice(byte[] pcm, long userId, long sequence)
        {
            User user;
            if (_users.TryGetValue((uint)userId, out user))
                Console.WriteLine(user.Name + " is speaking. Seq" + sequence);
        }
        #endregion

        public float TcpPing { get; private set; }

        public void Ping(MumbleSharp.Packets.Ping ping)
        {
            TcpPing = ping.TcpPingAvg;
        }

        public void TextMessage(MumbleSharp.Packets.TextMessage textMessage)
        {
            if (MessageRecieved == null) return;
            Message result;

            User user;
            if (!_users.TryGetValue(textMessage.Actor, out user))   //If we don't know the user for this packet, just ignore it
                return;

            if (textMessage.ChannelId == null)
            {
                if (textMessage.TreeId == null)
                {
                    //personal message: no channel, no tree
                    result = new PersonalMessage(user, string.Join("", textMessage.Message));
                }
                else
                {
                    //recursive message: sent to multiple channels
                    Channel channel;
                    if (!_channels.TryGetValue(textMessage.TreeId[0], out channel))    //If we don't know the channel for this packet, just ignore it
                        return;
                    result = new ChannelMessage(user, string.Join("", textMessage.Message), channel, true);
                }
            }
            else
            {
                Channel channel;
                if (!_channels.TryGetValue(textMessage.ChannelId[0], out channel))    //If we don't know the channel for this packet, just ignore it
                    return;

                result = new ChannelMessage(user, string.Join("", textMessage.Message), channel);
            }

            MessageRecieved(this, result);
        }

        public void UserList(MumbleSharp.Packets.UserList userList)
        {
        }


        public X509Certificate SelectCertificate(object sender, string targetHost, X509CertificateCollection localCertificates, X509Certificate remoteCertificate, string[] acceptableIssuers)
        {
            return null;
        }
    }
}