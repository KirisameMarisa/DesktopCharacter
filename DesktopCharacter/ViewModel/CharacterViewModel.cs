﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Livet;
using Livet.Commands;
using Livet.Messaging.Windows;
using Reactive.Bindings;
using System.Windows.Input;
using System.Reactive.Linq;
using Livet.Messaging;
using System.Collections.ObjectModel;
using DesktopCharacter.Model.Locator;
using NLog;
using System.Windows.Media.Imaging;
using BabumiGraphics.Graphics;
using BabumiGraphics.Live2D;
using System.IO;
using DesktopCharacter.Model.Repository;
using DesktopCharacter.Model.Database.Domain;
using SharpGL;
using System.Windows.Media;
using DesktopCharacter.Model;
using DesktopCharacter.Model.Graphics;
using DesktopCharacter.Util.Messenger.Message;
using DesktopCharacter.Util.Math;
using DesktopCharacter.Model.AI;

namespace DesktopCharacter.ViewModel
{
    class CharacterViewModel : Livet.ViewModel
    {
        /// <summary>
        /// 初期化フラグ
        /// </summary>
        private bool _initialized = false;
        /// <summary>
        /// スクリーンサイズ
        /// </summary>
        public Point ScreenSize { get; set; }
        /// <summary>
        /// モデル描画をまとめたModel層
        /// </summary>
        private Live2DManaged _model = new Live2DManaged();
        /// <summary>
        /// コンフィグファイルのリポジトリ
        /// </summary>
        private BabumiConfigRepository _babumiConfigRepository;
        /// <summary>
        /// マスコット画像のイメージ
        /// </summary>
        private ImageSource _source;
        public ImageSource Source
        {
            get { return _source; }
            set
            {
                _source = value;
                this.RaisePropertyChanged("Source");
            }
        }

        private ViewModelCommand _menuOpenCommand;
        public ViewModelCommand MenuOpenCommand
        {
            get
            {
                if (_menuOpenCommand == null)
                {
                    _menuOpenCommand = new ViewModelCommand(() =>
                    {
                        using (var vm = new Menu.MenuItemViewModel(this))
                        {
                            Messenger.Raise(new TransitionMessage(vm, "MenuItem"));
                        }
                    });
                }
                return _menuOpenCommand;
            }
        }

        private ViewModelCommand mDrawCommand;
        public ViewModelCommand DrawCommand
        {
            get
            {
                if (mDrawCommand == null)
                {
                    mDrawCommand = new ViewModelCommand( () =>
                    {
                        //!< キャラクターVMの初期化が終わるまで遅延させる
                        if (!_initialized)
                        {
                            return;
                        }
                        if (!_model.Initialized)
                        {
                            _model.Initialize(ScreenSize);
                        }
                        Source = _model.Draw();
                    });
                }
                return mDrawCommand;
            }
        }

        private ViewModelCommand mInitializeCommand;
        public ViewModelCommand InitializeCommand
        {
            get
            {
                if (mInitializeCommand == null)
                {
                    mInitializeCommand = new ViewModelCommand(() =>
                    {
                        //!< ここでモデルの初期化はしない
                        //!< 描画コマンドの実行時にはスクリーンサイズが取得できているのでそちらで行う
                        //!< コマンドとして定義している理由は、ビヘイビアでDeviceの初期化を行っているため
                        return; 
                    });
                }
                return mInitializeCommand;
            }
        }

        private Livet.Commands.ListenerCommand<object> _scaleChangeCommand;
        public Livet.Commands.ListenerCommand<object> ScaleChangeCommand
        {
            get
            {
                if (_scaleChangeCommand == null)
                {
                    _scaleChangeCommand = new ListenerCommand<object>((object sender) =>
                    {
                        var setting = _babumiConfigRepository.GetConfig();
                        var param = sender as MouseWheelEventArgs;
                        if (param != null)
                        {
                            if (param.Delta > 0)
                            {
                                setting.AddZoomLevel(+1);
                            }
                            if (param.Delta < 0)
                            {
                                setting.AddZoomLevel(-1);
                            }
                            try
                            {
                                CharacterNotify.Instance.WindowResizeMessage(setting.ZoomLevel);
                            }
                            catch (Exception e)
                            {
                                Messenger.Raise(new CloseMessage(true, e.Message, "Error"));
                            }
                            _babumiConfigRepository.Save(setting);
                        }
                    });
                }
                return _scaleChangeCommand;
            }
        }

        private Livet.Commands.ListenerCommand<object> _motionRunCommand;
        public Livet.Commands.ListenerCommand<object> MotionRunCommand
        {
            get
            {
                if (_motionRunCommand == null)
                {
                    _motionRunCommand = new ListenerCommand<object>((object sender) =>
                    {
                        var board = ServiceLocator.Instance.GetInstance<BlackBoard>();
                        //!< 思考に必要なものを記憶させる
                        board.TouchAction.IsClickAction = true;
                        board.TouchAction.MousePoint = (Util.Math.Point)sender;
                        board.TouchAction.SplitSize = ScreenSize.Y / 2.0;
                        //!< Behaviorを実行
                        new BehaviorTree().Update();
                    });
                }
                return _motionRunCommand;
            }
        }
        private Livet.Commands.ListenerCommand<object> _savePositionCommand;
        public Livet.Commands.ListenerCommand<object> SavePositionCommand
        {
            get
            {
                if (_savePositionCommand == null)
                {
                    _savePositionCommand = new ListenerCommand<object>((object sender) =>
                    {
                        var point = (Util.Math.Point)sender;
                        var repo = ServiceLocator.Instance.GetInstance<WindowPositionRepository>();
                        repo.Save((int)point.X, (int)point.Y);
                    });
                }
                return _savePositionCommand;
            }
        }
        
        public CharacterViewModel()
        {
            CharacterNotify.Instance.TopMostMessageSubject.Subscribe(TopMostMessageSend);
            CharacterNotify.Instance.WindowSizeMessageSubject.Subscribe(WindowSizeChange);
            _babumiConfigRepository = ServiceLocator.Instance.GetInstance<BabumiConfigRepository>();
        }

        public void Initialize()
        {
            //!< 初期化フラグOn
            _initialized = true;
            //!< コンフィグファイルを読み込む
            var setting = _babumiConfigRepository.GetConfig();
            try
            {
                CharacterNotify.Instance.WindowResizeMessage(setting.ZoomLevel);
            }
            catch (Exception e)
            {
                Messenger.Raise(new CloseMessage(true, e.Message, "Error"));
            }           
            //!< Windowの最前面かどうかをコンフィグから設定
            TopMostMessageSend(setting.Topmost);
            //!< 起動時に例外処理をしているので必ずnullではないと思うのだけど...
            Messenger.Raise(new CloseMessage( setting == null, "Configファイルを正しく読み込めてない可能性があるため終了します", "InfoMessage"));
            //!< GLのバージョンを表示してアプリケーションを終了する
            Messenger.Raise(new CloseMessage(
                setting.RequiredVersion > GraphicsManager.Instance.GetVersion(),
                string.Format("GL_VENDOR: {0} \nGL_RENDERER : {1} \nGL_VERSION : {2} \nOpenGLのバージョンが4.3以下です！\nコンピュートシェーダに対応していないため終了します",
                GraphicsManager.Instance.mVender,
                GraphicsManager.Instance.mRender,
                GraphicsManager.Instance.mVersion),
                "InfoMessage"));  
        }
        
        private void TopMostMessageSend( bool topmost )
        {
            //!< Windowの最前面かどうかをコンフィグから設定
            Messenger.Raise(new TopmostMessage("TopmostMessage", topmost));
        }

        private void WindowSizeChange(Point scaleSize )
        {
            ScreenSize = scaleSize;
            Messenger.Raise(new ReszieMessage("WindowResizeMessage", ScreenSize));
            _model.Destory();  
        }
    }
}
