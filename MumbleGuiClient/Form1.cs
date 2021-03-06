﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MumbleSharp;
using MumbleSharp.Model;

using Message = MumbleSharp.Model.Message;

namespace MumbleGuiClient
{
    public partial class Form1 : Form
    {
        private class AudioPlayer
        {
            private readonly NAudio.Wave.WaveOut _playbackDevice = new NAudio.Wave.WaveOut();

            public AudioPlayer(NAudio.Wave.IWaveProvider provider)
            {
                _playbackDevice.Init(provider);
                _playbackDevice.Play();

                _playbackDevice.PlaybackStopped += (sender, args) =>
                    {
                        //MessageBox.Show("stop");
                        //Console.WriteLine("Playback stopped: " + args.Exception);
                    };
            }
        }
        readonly Dictionary<User, AudioPlayer> _players = new Dictionary<User, AudioPlayer>(); 

        MumbleConnection connection;
        ConnectionMumbleProtocol protocol;
        MicrophoneRecorder recorder;

        bool tvUsersClick = false;
        int selectedDevice;

        struct ChannelInfo
        {
            public string Name;
            public uint Id;
            public uint Parent;
        }
        struct UserInfo 
        {
            public uint Id;
            public bool Deaf;
            public bool Muted;
            public bool SelfDeaf;
            public bool SelfMuted;
            public bool Supress;
            public uint Channel;
        }

        class TreeNode<T> : TreeNode
        {
            public T Value;
        }
        public Form1()
        {
            InitializeComponent();

            protocol = new ConnectionMumbleProtocol();
            protocol.channelMessageReceivedDelegate = ChannelMessageReceivedDelegate;
            protocol.personalMessageReceivedDelegate = PersonalMessageReceivedDelegate;
            protocol.encodedVoice = EncodedVoiceDelegate;
            protocol.userJoinedDelegate = UserJoinedDelegate;
            protocol.userLeftDelegate = UserLeftDelegate;
            protocol.channelJoinedDelegate = ChannelJoinedDelegate;
            protocol.channelLeftDelegate = ChannelLeftDelegate;
            protocol.serverConfigDelegate = ServerConfigDelegate;

            tvUsers.ExpandAll();

            recorder = new MicrophoneRecorder(protocol);
            int deviceCount = NAudio.Wave.WaveIn.DeviceCount;
            for (int i = 0; i < deviceCount; i++)
            {
                NAudio.Wave.WaveInCapabilities deviceInfo = NAudio.Wave.WaveIn.GetCapabilities(i);
                string deviceText = string.Format("{0}, {1} channels", deviceInfo.ProductName, deviceInfo.Channels);
                comboBox1.Items.Add(deviceText);
            }
            if (deviceCount > 0)
            {
                MicrophoneRecorder.SelectedDevice = 0;
                comboBox1.SelectedIndex = 0;
            }
        }

        UserInfo GetUserInfo(User user)
        {
            return new UserInfo
            {
                Id = user.Id,
                Deaf = user.Deaf,
                Muted = user.Muted,
                SelfDeaf = user.SelfDeaf,
                SelfMuted = user.SelfMuted,
                Supress = user.Suppress,
                Channel = user.Channel.Id
            };
        }
        ChannelInfo GetChannelInfo(Channel channel)
        {
            return new ChannelInfo
            {
                Name = channel.Name,
                Id = channel.Id,
                Parent = channel.Parent
            };
        }
        TreeNode GetUserNode(uint user_id, TreeNode rootNode)
        {
            foreach (TreeNode node in rootNode.Nodes)
            {
                if (node is TreeNode<UserInfo>) if (((TreeNode<UserInfo>)node).Value.Id == user_id)
                        return node;
                if (node is TreeNode<ChannelInfo>)
                {
                    TreeNode subNode = GetUserNode(user_id, node);
                    if (subNode != null) return subNode;
                }
            }

            return null;
        }
        TreeNode GetChannelNode(uint channel_id, TreeNode rootNode)
        {
            if (rootNode is TreeNode<ChannelInfo>)
                if (((TreeNode<ChannelInfo>)rootNode).Value.Id == channel_id)
                    return rootNode;

            foreach (TreeNode node in rootNode.Nodes)
            {
                if (node is TreeNode<ChannelInfo>)
                {
                    if (((TreeNode<ChannelInfo>)node).Value.Id == channel_id)
                        return node;

                    TreeNode subNode = GetChannelNode(channel_id, node);
                    if (subNode != null) return subNode;
                }
            }

            return null;
        }
        TreeNode MakeChannelNode(Channel channel)
        {
            TreeNode<ChannelInfo> result = new TreeNode<ChannelInfo>();
            result.Text = channel.Name;
            result.BackColor = Color.LightBlue;
            result.Value = GetChannelInfo(channel);

            return result;
        }
        TreeNode MakeUserNode(User user)
        {
            TreeNode<UserInfo> result = new TreeNode<UserInfo>();
            result.Text = user.Name;
            result.BackColor = Color.LightGreen;
            result.Value = GetUserInfo(user);

            return result;
        }
        bool DeleteUserNode(uint user_id, TreeNode rootNode)
        {
            TreeNode<UserInfo> user = null;

            foreach (TreeNode node in rootNode.Nodes)
            {
                if (node is TreeNode<UserInfo>) if (((TreeNode<UserInfo>)node).Value.Id == user_id)
                    {
                        user = node as TreeNode<UserInfo>;
                        break;
                    }
                if (node is TreeNode<ChannelInfo>)
                {
                    if (DeleteUserNode(user_id, node))
                        return true;
                }
            }

            if (user != null)
            {
                user.Remove();
                return true;
            }

            return false;

            return false;
        }
        bool DeleteChannelNode(uint channel_id, TreeNode rootNode)
        {
            if (rootNode is TreeNode<ChannelInfo>) if (((TreeNode<ChannelInfo>)rootNode).Value.Id == channel_id)
                {
                    rootNode.Remove();
                    return true;
                }

            TreeNode channelNode = null;

            foreach (TreeNode node in rootNode.Nodes)
            {
                if (node is TreeNode<ChannelInfo>)
                {
                    if (((TreeNode<ChannelInfo>)node).Value.Id == channel_id)
                    {
                        channelNode = node;
                        break;
                    }

                    if (DeleteUserNode(channel_id, node))
                        return true;
                }
            }

            if (channelNode != null)
            {
                channelNode.Remove();
                return true;
            }
            return false;
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            string message = tbSendMessage.Text;
            Channel target = protocol.LocalUser.Channel;
            tbLog.BeginInvoke((MethodInvoker)(() =>
            {
                tbLog.AppendText(string.Format("[{0:HH:mm:ss}] {1} to {2}: {3}\n", DateTime.Now, protocol.LocalUser.Name, protocol.LocalUser.Channel.Name, message));
            }));

            var msg = new MumbleProto.TextMessage
            {
                actor = protocol.LocalUser.Id,
                message = tbSendMessage.Text,
            };
            msg.channel_id.Add(target.Id);

            connection.SendControl<MumbleProto.TextMessage>(MumbleSharp.Packets.PacketType.TextMessage, msg);
            tbSendMessage.Text = "";
        }

        private void mumbleUpdater_Tick(object sender, EventArgs e)
        {
            if (connection != null)
                connection.Process();
        }

        private void tvUsers_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (tvUsers.SelectedNode is TreeNode<ChannelInfo>)
            {
                ChannelInfo channel = ((TreeNode<ChannelInfo>)tvUsers.SelectedNode).Value;
                //Enter that channel, needs the functionality in connection or protocol.

                protocol.Channels.SingleOrDefault(a => a.Id == channel.Id)?.Join();
            }
        }

        //--------------------------

        void EncodedVoiceDelegate(BasicMumbleProtocol proto, byte[] data, uint userId, long sequence, MumbleSharp.Audio.Codecs.IVoiceCodec codec, MumbleSharp.Audio.SpeechTarget target)
        {
            User user = proto.Users.FirstOrDefault(u => u.Id == userId);
            TreeNode<UserInfo> userNode = null;
            foreach (TreeNode<ChannelInfo> chanelNode in tvUsers.Nodes)
            {
                foreach (TreeNode<UserInfo> subNode in chanelNode.Nodes)
                    if (subNode.Value.Id == user.Id)
                        userNode = subNode;

                if (userNode != null)
                {
                    break;
                }
            }

            if (userNode != null)
            {
                //userNode.BeginInvoke((MethodInvoker)(() =>
                //    {
                //        userNode.Text = user.Name + " [SPEAK]";
                //    }));
            }
        }
        void ChannelJoinedDelegate(BasicMumbleProtocol proto, Channel channel)
        {
            TreeNode<ChannelInfo> channelNode = null;
            if (tvUsers.Nodes.Count > 0)
                channelNode = (TreeNode<ChannelInfo>)GetChannelNode(channel.Id, tvUsers.Nodes[0]);

            if (channelNode == null)
            {
                channelNode = (TreeNode<ChannelInfo>)MakeChannelNode(channel);

                TreeNode<ChannelInfo> channeParentlNode = null;
                if (channel.Id > 0)
                {
                    if (tvUsers.Nodes.Count > 0)
                        channeParentlNode = (TreeNode<ChannelInfo>)GetChannelNode(channel.Parent, tvUsers.Nodes[0]);
                }

                if (channeParentlNode == null)
                    tvUsers.Nodes.Add(channelNode);
                else
                    channeParentlNode.Nodes.Add(channelNode);
            }
        }
        void ChannelLeftDelegate(BasicMumbleProtocol proto, Channel channel)
        {
            DeleteChannelNode(channel.Id, tvUsers.Nodes[0]);
        }
        void UserJoinedDelegate(BasicMumbleProtocol proto, User user)
        {
            TreeNode<UserInfo> userNode = null;
            if (tvUsers.Nodes.Count > 0)
                userNode = (TreeNode<UserInfo>)GetUserNode(user.Id, tvUsers.Nodes[0]);

            if (userNode == null)
            {
                userNode = (TreeNode<UserInfo>)MakeUserNode(user);
                
                TreeNode channelNode = GetChannelNode(user.Channel.Id, tvUsers.Nodes[0]);
                if (channelNode == null)
                {
                    channelNode = MakeChannelNode(user.Channel);

                    TreeNode parentChannelNode = GetChannelNode(user.Channel.Parent, tvUsers.Nodes[0]);
                    parentChannelNode.Nodes.Add(channelNode);
                }
                channelNode.Nodes.Add(userNode);
            }
            else
            {
                if (userNode.Value.Channel != user.Channel.Id)
                {
                    TreeNode channelNode = GetChannelNode(userNode.Value.Channel, tvUsers.Nodes[0]);
                    channelNode.Nodes.Remove(userNode);

                    channelNode = GetChannelNode(user.Channel.Id, tvUsers.Nodes[0]);
                    channelNode.Nodes.Add(userNode);
                }

                userNode.Value = GetUserInfo(user);
            }

            if (!_players.ContainsKey(user))
                _players.Add(user, new AudioPlayer(user.Voice));
            else 
                _players[user] = new AudioPlayer(user.Voice);
        }
        void UserLeftDelegate(BasicMumbleProtocol proto, User user)
        {
            DeleteUserNode(user.Id, tvUsers.Nodes[0]);

            _players.Remove(user);
        }
        void ChannelMessageReceivedDelegate(BasicMumbleProtocol proto, ChannelMessage message)
        {
            if (message.Channel.Equals(proto.LocalUser.Channel))
                tbLog.BeginInvoke((MethodInvoker)(() =>
                {
                    tbLog.AppendText(string.Format("[{0:HH:mm:ss}] {1} to {2}: {3}\n", DateTime.Now, message.Sender.Name, message.Channel.Name, message.Text));
                }));
        }
        void PersonalMessageReceivedDelegate(BasicMumbleProtocol proto, PersonalMessage message)
        {
            tbLog.BeginInvoke((MethodInvoker)(() =>
            {
                tbLog.AppendText(string.Format("[{0:HH:mm:ss}] {1} to you: {2}\n", DateTime.Now, message.Sender.Name, message.Text));
            }));
        }
        void ServerConfigDelegate(BasicMumbleProtocol proto, MumbleProto.ServerConfig serverConfig)
        {
            tbLog.BeginInvoke((MethodInvoker)(() =>
            {
                tbLog.AppendText(string.Format("{0}\n", serverConfig.welcome_text));
            }));
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (recorder._recording)
            {
                button1.Text = "record";
                recorder.Stop();
            }
            else
            {
                button1.Text = "stop";
                recorder.Record();
            }
        }

        private void tvUsers_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            tvUsersClick = true;
        }

        private void tvUsers_BeforeCollapse(object sender, TreeViewCancelEventArgs e)
        {
            if (tvUsersClick)
            {
                tvUsersClick = false;
                e.Cancel = true;
            }
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            MicrophoneRecorder.SelectedDevice = comboBox1.SelectedIndex;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            string name = textBox1.Text;
            string pass = "";
            int port = 64738;
            string addr = textBox2.Text;

            if (connection != null)
            {
                connection.Close();
                connection = null;
                protocol.Close();
                tvUsers.Nodes.Clear();
            }

            connection = new MumbleConnection(new IPEndPoint(Dns.GetHostAddresses(addr).First(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork), port), protocol);
            connection.Connect(name, pass, new string[0], addr);

            while (connection.Protocol.LocalUser == null)
            {
                connection.Process();
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (connection != null)
            {
                connection.Close();
                connection = null;
                protocol.Close();
                tvUsers.Nodes.Clear();
            }
        }

        //----------------------------
    }
}
