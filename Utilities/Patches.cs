using HarmonyLib;
using Sandbox.Engine.Analytics;
using Sandbox.Engine.Multiplayer;
using Sandbox.Engine.Networking;
using Sandbox.Game;
using Sandbox.Game.Gui;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Sandbox.Game.World.Generator;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using VRage;
using VRage.FileSystem;
using VRage.Game;
using VRage.GameServices;
using VRage.Network;
using VRage.Utils;
using VRageMath;

namespace SeamlessClientPlugin.SeamlessTransfer
{
    public static class Patches
    {
        /* All internal classes Types */
        public static readonly Type ClientType = Type.GetType("Sandbox.Engine.Multiplayer.MyMultiplayerClient, Sandbox.Game");
        public static readonly Type SyncLayerType = Type.GetType("Sandbox.Game.Multiplayer.MySyncLayer, Sandbox.Game");
        public static readonly Type MyTransportLayerType = Type.GetType("Sandbox.Engine.Multiplayer.MyTransportLayer, Sandbox.Game");
        public static readonly Type MySessionType = Type.GetType("Sandbox.Game.World.MySession, Sandbox.Game");
        public static readonly Type VirtualClientsType = Type.GetType("Sandbox.Engine.Multiplayer.MyVirtualClients, Sandbox.Game");
        public static readonly Type GUIScreenChat = Type.GetType("Sandbox.Game.Gui.MyGuiScreenChat, Sandbox.Game");
        public static readonly Type MyMultiplayerClientBase = Type.GetType("Sandbox.Engine.Multiplayer.MyMultiplayerClientBase, Sandbox.Game");
        public static readonly Type MySteamServerDiscovery = Type.GetType("VRage.Steam.MySteamServerDiscovery, Vrage.Steam");

        /* Harmony Patcher */
        private static Harmony Patcher = new Harmony("SeamlessClientPatcher");


        /* Static Contructors */
        public static ConstructorInfo ClientConstructor { get; private set; }
        public static ConstructorInfo SyncLayerConstructor { get; private set; }
        public static ConstructorInfo TransportLayerConstructor { get; private set; }
        public static ConstructorInfo MySessionConstructor { get; private set; }
        public static ConstructorInfo MyMultiplayerClientBaseConstructor { get; private set; }



        /* Static FieldInfos and PropertyInfos */
        public static PropertyInfo MySessionLayer { get; private set; }
        public static FieldInfo VirtualClients { get; private set; }
        public static FieldInfo AdminSettings { get; private set; }
        public static FieldInfo RemoteAdminSettings { get; private set; }
        public static FieldInfo MPlayerGPSCollection { get; private set; }


        /* Static MethodInfos */
        public static MethodInfo InitVirtualClients { get; private set; }
        public static MethodInfo LoadPlayerInternal { get; private set; }
        public static MethodInfo LoadMembersFromWorld { get; private set; }
        public static MethodInfo LoadMultiplayer { get; private set; }

        public static MethodInfo SendPlayerData;


        public static event EventHandler<JoinResultMsg> OnJoinEvent;



        /* WorldGenerator */
        public static MethodInfo UnloadProceduralWorldGenerator;



        public static void GetPatches()
        {
            //Get reflected values and store them




            /* Get Constructors */
            ClientConstructor = GetConstructor(ClientType, BindingFlags.Instance | BindingFlags.NonPublic, new Type[2] { typeof(MyGameServerItem), SyncLayerType });
            SyncLayerConstructor = GetConstructor(SyncLayerType, BindingFlags.Instance | BindingFlags.NonPublic, new Type[1] { MyTransportLayerType });
            TransportLayerConstructor = GetConstructor(MyTransportLayerType, BindingFlags.Instance | BindingFlags.Public, new Type[1] { typeof(int) });
            MySessionConstructor = GetConstructor(MySessionType, BindingFlags.Instance | BindingFlags.NonPublic, new Type[2] { typeof(MySyncLayer), typeof(bool) });
            MyMultiplayerClientBaseConstructor = GetConstructor(MyMultiplayerClientBase, BindingFlags.Instance | BindingFlags.NonPublic, new Type[] { typeof(MySyncLayer) });


            /* Get Fields and Properties */
            MySessionLayer = GetProperty(typeof(MySession), "SyncLayer", BindingFlags.Instance | BindingFlags.Public);
            VirtualClients = GetField(typeof(MySession), "VirtualClients", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            AdminSettings = GetField(typeof(MySession), "m_adminSettings", BindingFlags.Instance | BindingFlags.NonPublic);
            RemoteAdminSettings = GetField(typeof(MySession), "m_remoteAdminSettings", BindingFlags.Instance | BindingFlags.NonPublic);
            MPlayerGPSCollection = GetField(typeof(MyPlayerCollection), "m_players", BindingFlags.Instance | BindingFlags.NonPublic);




            /* Get Methods */
            MethodInfo OnJoin = GetMethod(ClientType, "OnUserJoined", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo LoadingAction = GetMethod(typeof(MySessionLoader), "LoadMultiplayerSession", BindingFlags.Public | BindingFlags.Static);
            InitVirtualClients = GetMethod(VirtualClientsType, "Init", BindingFlags.Instance | BindingFlags.Public);
            LoadPlayerInternal = GetMethod(typeof(MyPlayerCollection), "LoadPlayerInternal", BindingFlags.Instance | BindingFlags.NonPublic);
            LoadMembersFromWorld = GetMethod(typeof(MySession), "LoadMembersFromWorld", BindingFlags.NonPublic | BindingFlags.Instance);
            LoadMultiplayer = GetMethod(typeof(MySession), "LoadMultiplayer", BindingFlags.Static | BindingFlags.NonPublic);
            SendPlayerData = GetMethod(ClientType, "SendPlayerData", BindingFlags.Instance | BindingFlags.NonPublic);
            UnloadProceduralWorldGenerator = GetMethod(typeof(MyProceduralWorldGenerator), "UnloadData", BindingFlags.Instance | BindingFlags.NonPublic);




            MethodInfo ConnectToServer = GetMethod(typeof(MyGameService), "ConnectToServer", BindingFlags.Static | BindingFlags.Public);
            MethodInfo LoadingScreenDraw = GetMethod(typeof(MyGuiScreenLoading), "DrawInternal", BindingFlags.Instance | BindingFlags.NonPublic);




            Patcher.Patch(LoadingScreenDraw, prefix: new HarmonyMethod(GetPatchMethod(nameof(DrawInternal))));
            Patcher.Patch(OnJoin, postfix: new HarmonyMethod(GetPatchMethod(nameof(OnUserJoined))));
            Patcher.Patch(LoadingAction, prefix: new HarmonyMethod(GetPatchMethod(nameof(LoadMultiplayerSession))));
            //Patcher.Patch(ConnectToServer, prefix: new HarmonyMethod(GetPatchMethod(nameof(OnConnectToServer))));
        }

        private static MethodInfo GetPatchMethod(string v)
        {
            return typeof(Patches).GetMethod(v, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }

        #region LoadingScreen

        /* Loading Screen Stuff */

        private static string LoadingScreenTexture = null;
        private static string ServerName;

        private static bool LoadMultiplayerSession(MyObjectBuilder_World world, MyMultiplayerBase multiplayerSession)
        {


            MyLog.Default.WriteLine("LoadSession() - Start");
            if (!MyWorkshop.CheckLocalModsAllowed(world.Checkpoint.Mods, allowLocalMods: false))
            {
                MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Error, MyMessageBoxButtonsType.OK, messageCaption: MyTexts.Get(MyCommonTexts.MessageBoxCaptionError), messageText: MyTexts.Get(MyCommonTexts.DialogTextLocalModsDisabledInMultiplayer)));
                MyLog.Default.WriteLine("LoadSession() - End");
                return false;
            }
            MyWorkshop.DownloadModsAsync(world.Checkpoint.Mods, delegate (bool success)
            {
                if (success)
                {
                    MyScreenManager.CloseAllScreensNowExcept(null);
                    MyGuiSandbox.Update(16);
                    if (MySession.Static != null)
                    {
                        MySession.Static.Unload();
                        MySession.Static = null;
                    }

                    ServerName = multiplayerSession.HostName;
                    GetCustomLoadingScreenPath(world.Checkpoint.Mods, out LoadingScreenTexture);


                    MySessionLoader.StartLoading(delegate
                    {

                        LoadMultiplayer.Invoke(null, new object[] { world, multiplayerSession });
                        //MySession.LoadMultiplayer(world, multiplayerSession);
                    }, null, null, null);
                }
                else
                {
                    multiplayerSession.Dispose();
                    MySessionLoader.UnloadAndExitToMenu();
                    if (MyGameService.IsOnline)
                    {
                        MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Error, MyMessageBoxButtonsType.OK, messageCaption: MyTexts.Get(MyCommonTexts.MessageBoxCaptionError), messageText: MyTexts.Get(MyCommonTexts.DialogTextDownloadModsFailed)));
                    }
                    else
                    {
                        MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Error, MyMessageBoxButtonsType.OK, messageCaption: MyTexts.Get(MyCommonTexts.MessageBoxCaptionError), messageText: new StringBuilder(string.Format(MyTexts.GetString(MyCommonTexts.DialogTextDownloadModsFailedSteamOffline), MySession.GameServiceName))));
                    }
                }
                MyLog.Default.WriteLine("LoadSession() - End");
            }, delegate
            {
                multiplayerSession.Dispose();
                MySessionLoader.UnloadAndExitToMenu();
            });

            return false;
        }

        private static bool DrawInternal(MyGuiScreenLoading __instance)
        {

            //If we dont have a custom loading screen texture, do not do the special crap below
            if (string.IsNullOrEmpty(LoadingScreenTexture))
                return true;


            float m_transitionAlpha = (float)typeof(MyGuiScreenBase).GetField("m_transitionAlpha", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(__instance);
            string m_font = "LoadingScreen";
            //MyGuiControlMultilineText m_multiTextControl = (MyGuiControlMultilineText)typeof(MyGuiScreenLoading).GetField("m_multiTextControl", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(__instance);


            Color color = new Color(255, 255, 255, 250);
            color.A = (byte)((float)(int)color.A * m_transitionAlpha);
            Rectangle fullscreenRectangle = MyGuiManager.GetFullscreenRectangle();
            MyGuiManager.DrawSpriteBatch("Textures\\GUI\\Blank.dds", fullscreenRectangle, Color.Black, false, true);
            Rectangle outRect;
            MyGuiManager.GetSafeHeightFullScreenPictureSize(MyGuiConstants.LOADING_BACKGROUND_TEXTURE_REAL_SIZE, out outRect);
            MyGuiManager.DrawSpriteBatch(LoadingScreenTexture, outRect, new Color(new Vector4(1f, 1f, 1f, m_transitionAlpha)), true, true);
            MyGuiManager.DrawSpriteBatch("Textures\\Gui\\Screens\\screen_background_fade.dds", outRect, new Color(new Vector4(1f, 1f, 1f, m_transitionAlpha)), true, true);

            //MyGuiSandbox.DrawGameLogoHandler(m_transitionAlpha, MyGuiManager.ComputeFullscreenGuiCoordinate(MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, 44, 68));

            string LoadScreen = $"Loading into {ServerName}! Please wait!";


            MyGuiManager.DrawString(m_font, LoadScreen, new Vector2(0.5f, 0.95f), MyGuiSandbox.GetDefaultTextScaleWithLanguage() * 1.1f, new Color(MyGuiConstants.LOADING_PLEASE_WAIT_COLOR * m_transitionAlpha), MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM);

            MyGuiManager.DrawString(m_font, "Nexus & SeamlessClient Made by: Casimir", new Vector2(0.95f, 0.95f), MyGuiSandbox.GetDefaultTextScaleWithLanguage() * 1.1f, new Color(MyGuiConstants.LOADING_PLEASE_WAIT_COLOR * m_transitionAlpha), MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM);

            /*
            if (string.IsNullOrEmpty(m_customTextFromConstructor))
            {
                string font = m_font;
                Vector2 positionAbsoluteBottomLeft = m_multiTextControl.GetPositionAbsoluteBottomLeft();
                Vector2 textSize = m_multiTextControl.TextSize;
                Vector2 normalizedCoord = positionAbsoluteBottomLeft + new Vector2((m_multiTextControl.Size.X - textSize.X) * 0.5f + 0.025f, 0.025f);
                MyGuiManager.DrawString(font, m_authorWithDash.ToString(), normalizedCoord, MyGuiSandbox.GetDefaultTextScaleWithLanguage());
            }
            */


            //m_multiTextControl.Draw(1f, 1f);

            return false;
        }



        private static bool GetCustomLoadingScreenPath(List<MyObjectBuilder_Checkpoint.ModItem> Mods, out string File)
        {
            File = null;
            string WorkshopDir = MyFileSystem.ModsPath;
            List<string> backgrounds = new List<string>();
            Random r = new Random(DateTime.Now.Millisecond);
            SeamlessClient.TryShow(WorkshopDir);
            try
            {
                SeamlessClient.TryShow("Installed Mods: " + Mods);
                foreach (var Mod in Mods)
                {
                    string SearchDir = Mod.GetPath();

                    if (!Directory.Exists(SearchDir))
                        continue;


                    var files = Directory.GetFiles(SearchDir, "CLB*.dds", SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        // Adds all files containing CustomLoadingBackground to a list for later randomisation
                        SeamlessClient.TryShow(Mod.FriendlyName + " contains a custom loading background!");
                        backgrounds.Add(file);

                    }
                }

                // Randomly pick a loading screen from the available backgrounds
                var rInt = r.Next(0, backgrounds.Count() - 1);
                File = backgrounds[rInt];
                return true;
            }
            catch (Exception ex)
            {
                SeamlessClient.TryShow(ex.ToString());
            }

            SeamlessClient.TryShow("No installed custom loading screen!");
            return false;
        }

        #endregion


        private static void OnUserJoined(ref JoinResultMsg msg)
        {
            if (msg.JoinResult == JoinResult.OK)
            {
                //SeamlessClient.TryShow("User Joined! Result: " + msg.JoinResult.ToString());

                //Invoke the switch event
                OnJoinEvent?.Invoke(null, msg);
            }
        }

        private static bool OnConnectToServer(MyGameServerItem server, Action<JoinResult> onDone)
        {
            if (SeamlessClient.IsSwitching)
                return false;


            return true;
        }





        private static MethodInfo GetMethod(Type type, string MethodName, BindingFlags Flags)
        {
            try
            {
                MethodInfo FoundMethod = type.GetMethod(MethodName, Flags);

                if (FoundMethod == null)
                    throw new NullReferenceException($"Method for {MethodName} is null!");


                return FoundMethod;

            }
            catch (Exception Ex)
            {
                throw Ex;
            }

        }

        private static FieldInfo GetField(Type type, string FieldName, BindingFlags Flags)
        {
            try
            {
                FieldInfo FoundField = type.GetField(FieldName, Flags);

                if (FoundField == null)
                    throw new NullReferenceException($"Field for {FieldName} is null!");


                return FoundField;

            }
            catch (Exception Ex)
            {
                throw Ex;
            }

        }

        private static PropertyInfo GetProperty(Type type, string PropertyName, BindingFlags Flags)
        {
            try
            {
                PropertyInfo FoundProperty = type.GetProperty(PropertyName, Flags);

                if (FoundProperty == null)
                    throw new NullReferenceException($"Property for {PropertyName} is null!");


                return FoundProperty;

            }
            catch (Exception Ex)
            {
                throw Ex;
            }

        }

        private static ConstructorInfo GetConstructor(Type type, BindingFlags Flags, Type[] Types)
        {

            try
            {
                ConstructorInfo FoundConstructor = type.GetConstructor(Flags, null, Types, null);

                if (FoundConstructor == null)
                    throw new NullReferenceException($"Contructor for {type.Name} is null!");


                return FoundConstructor;

            }
            catch (Exception Ex)
            {
                throw Ex;
            }


        }


    }
}
