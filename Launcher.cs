using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EverBound {
    public sealed class GoldButton : Button {
        public bool Primary, Selected;
        bool hover;
        public GoldButton() {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;
            ForeColor=Color.FromArgb(223,216,193);BackColor=Color.FromArgb(20,23,21);
            MouseEnter+=(s,e)=>{hover=true;Invalidate();};MouseLeave+=(s,e)=>{hover=false;Invalidate();};
        }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            var r=new Rectangle(1,1,Width-3,Height-3);
            var top=Primary ? Color.FromArgb(188,155,87) : (hover||Selected ? Color.FromArgb(47,47,37):BackColor);
            var bottom=Primary ? Color.FromArgb(132,102,49) : top;
            if(!Enabled)top=bottom=Color.FromArgb(38,40,35);
            using(var brush=new LinearGradientBrush(r,top,bottom,90f))g.FillRectangle(brush,r);
            using(var pen=new Pen(Primary&&Enabled?Color.FromArgb(222,192,127):Color.FromArgb(78,76,56)))g.DrawRectangle(pen,r);
            if(Primary)using(var pen=new Pen(Color.FromArgb(80,55,30)))g.DrawRectangle(pen,5,5,Width-11,Height-11);
            var color=!Enabled?Color.FromArgb(130,132,117):Primary?Color.FromArgb(23,24,20):ForeColor;
            TextRenderer.DrawText(g,Text,Font,r,color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            if(Focused)using(var pen=new Pen(Color.FromArgb(220,208,166)) { DashStyle=DashStyle.Dot })g.DrawRectangle(pen,8,8,Width-17,Height-17);
        }
    }
    public sealed class MainForm : Form {
        static readonly Color Bg=Color.FromArgb(15,19,18), Gold=Color.FromArgb(180,151,90), Cream=Color.FromArgb(232,226,208), Muted=Color.FromArgb(145,151,137), Green=Color.FromArgb(151,180,134);
        readonly Dictionary<Control,Rectangle> boxes=new Dictionary<Control,Rectangle>();
        readonly Dictionary<Control,Font> fonts=new Dictionary<Control,Font>();
        readonly List<Control> pageControls=new List<Control>();
        readonly List<GoldButton> navigation=new List<GoldButton>();
        readonly string storage, settingsFile;
        readonly Settings settings;
        readonly ReleaseClient client;
        readonly Installer installer;
        readonly Image art;
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        ReleaseData release;
        string page="Início", status="Preparando o EverBound…", version="2.0.0";
        bool busy, running, preview, closing;
        double fraction;
        CancellationTokenSource cancellation;
        GoldButton primary, cancelButton;
        Label stateLabel, metaLabel, sourceLabel;
        TextBox gameBox, profileBox, sourceBox;
        float scale=1;
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);

        public MainForm(string dataDirectory,bool renderOnly) {
            storage=dataDirectory;settingsFile=Path.Combine(storage,"settings.json");preview=renderOnly;
            settings=LoadSettings();
            client=new ReleaseClient(Data.Utf8.GetString(Data.Resource("release-public.xml")));
            installer=new Installer(storage);
            release=client.Included(); version=release.Manifest.version;
            using(var stream=new MemoryStream(Data.Resource("world.png")))art=Image.FromStream(stream).Clone() as Image;
            Text="EverBound Launcher"; FormBorderStyle=FormBorderStyle.None; StartPosition=FormStartPosition.CenterScreen;
            ClientSize=new Size(1160,760); MinimumSize=new Size(870,570); BackColor=Bg; ForeColor=Cream;
            AutoScaleMode=AutoScaleMode.None;DoubleBuffered=true;
            try { using(var icon=new MemoryStream(Data.Resource("launcher.ico")))Icon=new Icon(icon); } catch { }
            Font=new Font("Segoe UI",14,FontStyle.Regular,GraphicsUnit.Pixel);
            var area=Screen.PrimaryScreen.WorkingArea;
            if(area.Width<1200||area.Height<800)ClientSize=new Size(Math.Min(1160,area.Width-32),Math.Min(760,area.Height-32));
            var minimize=Button("—",1056,16,36,30,()=>WindowState=FormWindowState.Minimized,false);
            minimize.AccessibleName="Minimizar";
            var close=Button("×",1100,16,36,30,()=>Close(),false);close.AccessibleName="Fechar";
            var names=new[]{"Início","Instalação","Atualizações","Configurações"};
            for(int i=0;i<names.Length;++i) {
                var name=names[i];var button=Button(name,26,228+i*58,156,43,()=>SetPage(name),false);
                navigation.Add(button);
            }
            Button("Pasta do mod",26,638,156,35,()=>OpenFolder(settings.ProfileDirectory),false);
            Button("GitHub do projeto  ↗",26,684,156,34,()=>OpenLink(Data.Site),false);
            MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left&&e.Y<70*scale){ReleaseCapture();SendMessage(Handle,0xa1,new IntPtr(2),IntPtr.Zero);}};
            Resize+=(s,e)=>LayoutControls();
            FormClosing+=(s,e)=>{
                if(busy) { e.Cancel=true;status="Aguarde a operação terminar ou cancele o download.";Invalidate();return; }
                closing=true;timer.Stop();
            };
            timer.Interval=1200;timer.Tick+=(s,e)=>RefreshState();
            Shown+=async(s,e)=>{
                if(preview)return;
                timer.Start();
                if(String.IsNullOrEmpty(settings.GameDirectory))try {
                    var found=await Task.Run(()=>Game.Find().FirstOrDefault());
                    if(!String.IsNullOrEmpty(found)) { settings.GameDirectory=found;SaveSettings();if(page=="Instalação")SetPage(page); }
                }catch {status="Use Localizar… em Instalação para escolher seu jogo.";}
                await Check(false);
            };
            SetPage("Início");RefreshState();
        }
        Settings LoadSettings() {
            Settings result=null;
            try { if(File.Exists(settingsFile))result=Data.Parse<Settings>(File.ReadAllText(settingsFile,Data.Utf8)); }catch { }
            if(result==null)result=new Settings();
            if(String.IsNullOrWhiteSpace(result.ProfileDirectory))result.ProfileDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EverBound","Client");
            if(String.IsNullOrWhiteSpace(result.UpdateSource))result.UpdateSource=Data.DefaultSource;
            return result;
        }
        void SaveSettings() { if(!preview)Data.AtomicText(settingsFile,Data.Json(settings)); }
        T Place<T>(T control,int x,int y,int w,int h,bool content) where T:Control {
            boxes.Add(control,new Rectangle(x,y,w,h));fonts.Add(control,control.Font);Controls.Add(control);if(content)pageControls.Add(control);LayoutControls();return control;
        }
        Label Label(string text,int x,int y,int w,int h,float size,Color color,bool serif) {
            var label=new Label {Text=text,ForeColor=color,BackColor=Color.Transparent,Font=new Font(serif?"Georgia":"Segoe UI",size,FontStyle.Regular,GraphicsUnit.Pixel),AutoSize=false};
            return Place(label,x,y,w,h,true);
        }
        GoldButton Button(string text,int x,int y,int w,int h,Action action,bool content) {
            var button=new GoldButton {Text=text,Font=new Font("Segoe UI",14,FontStyle.Regular,GraphicsUnit.Pixel)};
            button.Click+=(s,e)=>{try{action();}catch(Exception ex){Error(ex);}};
            return Place(button,x,y,w,h,content);
        }
        TextBox Field(string text,int x,int y,int w) {
            return Place(new TextBox {Text=text??"",Font=new Font("Segoe UI",15,FontStyle.Regular,GraphicsUnit.Pixel),BackColor=Color.FromArgb(24,29,26),ForeColor=Cream,BorderStyle=BorderStyle.FixedSingle},x,y,w,32,true);
        }
        void LayoutControls() {
            scale=Math.Min(ClientSize.Width/1160f,ClientSize.Height/760f);
            foreach(var item in boxes) {
                var r=item.Value;item.Key.Bounds=new Rectangle((int)(r.X*scale),(int)(r.Y*scale),(int)(r.Width*scale),(int)(r.Height*scale));
                var f=fonts[item.Key];if(Math.Abs(item.Key.Font.Size-f.Size*scale)>0.1f)item.Key.Font=new Font(f.FontFamily,f.Size*scale,f.Style,GraphicsUnit.Pixel);
            }
            Invalidate();
        }
        void SetPage(string name) {
            if(busy)return;
            foreach(var control in pageControls) {boxes.Remove(control);fonts.Remove(control);Controls.Remove(control);control.Dispose();}pageControls.Clear();
            primary=null;cancelButton=null;stateLabel=null;metaLabel=null;sourceLabel=null;gameBox=null;profileBox=null;sourceBox=null;
            page=name;
            foreach(var nav in navigation) {nav.Selected=nav.Text==name;nav.Invalidate();}
            if(name=="Início")Home();else if(name=="Instalação")Installation();else if(name=="Atualizações")Updates();else Preferences();
            if(primary!=null)AcceptButton=primary;
            RefreshState();
        }
        void Home() {
            Label("ELDEN RING  /  EVERBOUND 2.0",250,111,650,22,12,Gold,false);
            Label("EVERBOUND",246,158,760,85,69,Cream,true);
            Label("Volte às Terras Intermédias.",251,258,570,40,25,Cream,true);
            Label("Prepare seu jogo. Continue sua jornada.",251,307,600,26,16,Muted,false);
            Label("HUD  ·  PROGRESSÃO  ·  INVENTÁRIO  ·  CHAT",251,384,730,24,12,Gold,false);
            Label("SEU EVERBOUND",250,479,600,23,12,Gold,false);
            stateLabel=Label("",250,519,550,38,29,Cream,true);
            metaLabel=Label("",251,569,730,26,14,Muted,false);
            Label("Instalação e atualizações sem substituir o executável do jogo.",251,609,760,25,13,Muted,false);
            Button("Verificar arquivos",250,662,182,44,()=>Run(VerifyFiles),true);
            Button("Atualizações",445,662,154,44,()=>SetPage("Atualizações"),true);
            primary=Button("JOGAR EVERBOUND",826,652,282,58,()=>Run(Primary),true);primary.Primary=true;
        }
        void Installation() {
            Label("PREPARAR O JOGO",250,111,700,23,12,Gold,false);
            Label("Sua jornada começa aqui.",248,153,800,52,38,Cream,true);
            Label("Localize sua cópia de Elden Ring e escolha onde guardar o EverBound.",250,218,830,28,16,Muted,false);
            Label("PASTA DO ELDEN RING",251,286,700,23,12,Gold,false);
            gameBox=Field(settings.GameDirectory,250,321,675);gameBox.ReadOnly=true;gameBox.AccessibleName="Pasta do Elden Ring";
            Button("Localizar…",945,319,163,36,ChooseGame,true);
            Label("Selecione o arquivo eldenring.exe. Compatível com a versão 1.17.",251,370,840,25,14,Muted,false);
            Label("PASTA DO EVERBOUND",251,428,700,23,12,Gold,false);
            profileBox=Field(settings.ProfileDirectory,250,465,675);profileBox.ReadOnly=true;profileBox.AccessibleName="Pasta do EverBound";
            Button("Alterar…",945,463,163,36,ChooseProfile,true);
            Label("Seus saves continuam na pasta normal do jogo. Atualizações preservam\nseu progresso, convite do chat e preferências.",251,517,840,57,15,Muted,false);
            stateLabel=Label("",251,593,820,29,17,Green,false);
            primary=Button("INSTALAR / REPARAR",826,652,282,58,()=>Run(Install),true);primary.Primary=true;
            Button("Detectar jogo",250,662,182,44,()=>Run(DetectGame),true);
        }
        void Updates() {
            Label("ATUALIZAÇÕES",250,111,700,23,12,Gold,false);
            Label("Mantenha seu mundo pronto.",248,153,850,52,36,Cream,true);
            metaLabel=Label("",251,221,850,27,15,Muted,false);
            Label("NOVIDADES DESTA VERSÃO",251,291,700,23,12,Gold,false);
            var notes=release.Manifest.notes.Take(6).Select((n,i)=>(i+1).ToString("00")+"   "+n);
            Label(String.Join("\n\n",notes),251,334,825,240,16,Cream,false);
            sourceLabel=Label("",251,596,840,25,13,Muted,false);
            stateLabel=Label("",251,626,550,24,14,Green,false);
            Button("Buscar atualização",250,662,194,44,()=>Run(()=>Check(true)),true);
            primary=Button("ATUALIZAR / REPARAR",826,652,282,58,()=>Run(Install),true);primary.Primary=true;
        }
        void Preferences() {
            Label("CONFIGURAÇÕES",250,111,700,23,12,Gold,false);
            Label("Do seu jeito.",248,153,800,52,38,Cream,true);
            Label("ATUALIZAÇÕES DO EVERBOUND",251,247,700,23,12,Gold,false);
            sourceBox=Field(settings.UpdateSource,250,283,858);sourceBox.AccessibleName="Endereço do catálogo de atualizações";
            Label("O launcher procura as versões publicadas no GitHub do projeto.\nVocê também pode selecionar um catálogo local para instalar sem internet.",251,332,860,56,15,Muted,false);
            Button("Selecionar catálogo local…",250,407,260,43,ChooseCatalog,true);
            Button("Restaurar GitHub",527,407,205,43,()=>{sourceBox.Text=Data.DefaultSource;},true);
            Label("CHAT ENTRE JOGADORES",251,497,700,23,12,Gold,false);
            Label("Importe seu convite individual para conectar o chat ao servidor da comunidade.",251,533,850,49,15,Muted,false);
            Button("Importar convite…",250,591,218,43,ImportInvite,true);
            Button("Cópias de segurança",486,591,230,43,()=>{Directory.CreateDirectory(Path.Combine(storage,"backups"));OpenFolder(Path.Combine(storage,"backups"));},true);
            primary=Button("SALVAR CONFIGURAÇÕES",826,652,282,58,SavePreferences,true);primary.Primary=true;
        }
        void RefreshState() {
            if(closing)return;
            if(!preview)running=Game.Running();
            bool present=Game.Present(settings.ProfileDirectory);
            var installed=Installer.State(settings.ProfileDirectory);var installedVersion=installed==null ? "2.0 · perfil existente" : installed.version;
            if(primary!=null) {
                primary.Enabled=!busy && (!running||page=="Configurações");
                if(page=="Início")primary.Text=running?"JOGO EM EXECUÇÃO":present?"JOGAR EVERBOUND":"INSTALAR EVERBOUND";
                primary.Invalidate();
            }
            if(stateLabel!=null) {
                if(page=="Início")stateLabel.Text=running?"Seu jogo está aberto.":present?"Pronto para jogar.":"Vamos preparar seu jogo.";
                else if(page=="Instalação")stateLabel.Text=running?"Feche o jogo pelo menu para instalar ou atualizar.":present?"Perfil encontrado. Você pode reparar ou atualizar.":"Tudo em uma pasta separada. Pronto para instalar.";
                else if(page=="Atualizações")stateLabel.Text=present&&installed!=null&&installed.version==release.Manifest.version?"Você está na versão atual.":"Pacote disponível: "+release.Manifest.version;
            }
            if(metaLabel!=null)metaLabel.Text=page=="Início"?(present?"Cliente "+installedVersion+"     •     Elden Ring 1.17":"Instalação guiada     •     Elden Ring 1.17"):
                "Instalado: "+(present?installedVersion:"ainda não instalado")+"     /     Disponível: "+release.Manifest.version;
            if(sourceLabel!=null)sourceLabel.Text=release.Embedded?"Pacote incluído no launcher · disponível sem internet":"Fonte: "+(release.Source.StartsWith("https://")?"GitHub / catálogo de atualizações":"catálogo local");
            Invalidate();
        }
        async void Run(Func<Task> operation) { if(busy)return;try{await operation();}catch(Exception ex){if(cancellation!=null&&cancellation.IsCancellationRequested){status="Operação cancelada. A instalação atual foi preservada.";}else Error(ex);}finally{busy=false;if(cancellation!=null){cancellation.Dispose();cancellation=null;}RemoveCancel();foreach(var nav in navigation)nav.Enabled=true;if(page=="Atualizações")SetPage(page);else RefreshState();} }
        void SetBusy(string text) {busy=true;status=text;fraction=0;foreach(var nav in navigation)nav.Enabled=false;RefreshState();}
        async Task Primary() { if(!Game.Present(settings.ProfileDirectory)){SetPage("Instalação");return;} SetBusy("Abrindo Elden Ring com o EverBound…");await Game.Launch(settings);status="EverBound iniciado. Boa jornada.";fraction=1; }
        async Task Check(bool requested) {
            if(busy)return;
            if(requested)SetBusy("Buscando atualização…");
            try {
                var latest=await client.Check(settings.UpdateSource,CancellationToken.None);
                release=latest;
                if(requested)status="Catálogo atualizado · EverBound "+release.Manifest.version+".";
                else if(!busy)status="EverBound "+release.Manifest.version+" disponível no GitHub.";
                if(page=="Atualizações"&&!busy)SetPage(page);
            } catch(Exception) {
                if(requested)status="Não consegui acessar as atualizações. O pacote incluído continua disponível.";
                else if(!busy)status="Pronto. O pacote incluído também funciona sem internet.";
            }
            RefreshState();
        }
        async Task Install() {
            if(running)throw new InvalidOperationException("Feche o Elden Ring pelo menu antes de instalar ou atualizar.");
            if(String.IsNullOrEmpty(settings.GameDirectory)){ChooseGame();if(String.IsNullOrEmpty(settings.GameDirectory))return;}
            settings.GameDirectory=Game.ValidateDirectory(settings.GameDirectory);SaveSettings();
            var installed=Installer.State(settings.ProfileDirectory);
            if(installed!=null&&new Version(installed.version)>new Version(release.Manifest.version))throw new InvalidOperationException("O pacote selecionado é mais antigo que sua instalação. Busque a versão mais recente.");
            SetBusy("Preparando instalação…");cancellation=new CancellationTokenSource();
            cancelButton=Button("Cancelar download",620,662,185,44,()=>{if(cancellation!=null)cancellation.Cancel();},true);
            var progress=new Progress<ProgressInfo>(p=>{status=p.Text;fraction=p.Fraction;if(cancelButton!=null)cancelButton.Enabled=p.CanCancel;Invalidate();});
            Directory.CreateDirectory(Path.Combine(storage,"downloads"));
            var zip=Path.Combine(storage,"downloads",Guid.NewGuid().ToString("N")+".zip");
            try {
                var selected=release;
                await client.Download(selected,zip,progress,cancellation.Token);
                await Task.Run(()=>installer.Apply(zip,selected,settings.ProfileDirectory,settings.GameDirectory,progress,cancellation.Token));
                status="EverBound "+selected.Manifest.version+" instalado. Pronto para jogar.";fraction=1;
            } finally {if(File.Exists(zip))File.Delete(zip);}
        }
        async Task VerifyFiles() {
            if(!Game.Present(settings.ProfileDirectory)){SetPage("Instalação");return;}
            SetBusy("Verificando arquivos do EverBound…");
            var result=await Task.Run(()=>Installer.Verify(settings.ProfileDirectory,release.Manifest));
            fraction=1;
            if(result.Count==0)status="Arquivos verificados. Seu EverBound está completo.";
            else {status=result.Count+" arquivo(s) precisam ser reparados. Use Instalar / reparar.";MessageBox.Show(this,status,"Verificação do EverBound",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        }
        async Task DetectGame() {SetBusy("Localizando Elden Ring…");var found=await Task.Run(()=>Game.Find().FirstOrDefault());if(found==null){status="Não encontrei o jogo automaticamente. Use Localizar…";return;}settings.GameDirectory=found;SaveSettings();if(gameBox!=null)gameBox.Text=found;status="Pasta do Elden Ring encontrada.";fraction=1;}
        void ChooseGame() {
            if(busy)return;
            using(var dialog=new OpenFileDialog {Title="Selecione eldenring.exe",Filter="Elden Ring|eldenring.exe",CheckFileExists=true}) {
                if(Directory.Exists(settings.GameDirectory))dialog.InitialDirectory=settings.GameDirectory;
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                settings.GameDirectory=Game.ValidateDirectory(Path.GetDirectoryName(dialog.FileName));SaveSettings();if(gameBox!=null)gameBox.Text=settings.GameDirectory;
                status="Pasta do jogo configurada.";RefreshState();
            }
        }
        void ChooseProfile() {
            if(busy||running)throw new InvalidOperationException("Feche o jogo antes de alterar a pasta do mod.");
            using(var dialog=new FolderBrowserDialog {Description="Escolha a pasta do EverBound, separada da pasta do Elden Ring.",SelectedPath=settings.ProfileDirectory,ShowNewFolderButton=true}) {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                Data.AssertNoLinks(dialog.SelectedPath);settings.ProfileDirectory=dialog.SelectedPath;SaveSettings();if(profileBox!=null)profileBox.Text=settings.ProfileDirectory;RefreshState();
            }
        }
        void ChooseCatalog() {using(var dialog=new OpenFileDialog {Title="Selecione manifest.json do EverBound",Filter="Catálogo do EverBound|manifest.json",CheckFileExists=true}){if(dialog.ShowDialog(this)==DialogResult.OK)sourceBox.Text=dialog.FileName;}}
        void SavePreferences() {
            var text=sourceBox.Text.Trim();Uri uri;
            if(Uri.TryCreate(text,UriKind.Absolute,out uri)&&!uri.IsFile&&(uri.Scheme!="https"||!String.IsNullOrEmpty(uri.UserInfo)))throw new InvalidOperationException("Use um endereço HTTPS sem senha ou um catálogo local.");
            if(String.IsNullOrWhiteSpace(text))text=Data.DefaultSource;
            settings.UpdateSource=text;SaveSettings();status="Configurações salvas.";fraction=0;Invalidate();
        }
        void ImportInvite() {using(var dialog=new OpenFileDialog {Title="Convite individual do chat EverBound",Filter="Convite do chat|*.json",CheckFileExists=true}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;Game.ImportInvite(dialog.FileName,settings.ProfileDirectory);status="Convite importado. O chat se conectará na próxima abertura do jogo.";Invalidate();}}
        void RemoveCancel() {if(cancelButton==null)return;boxes.Remove(cancelButton);fonts.Remove(cancelButton);pageControls.Remove(cancelButton);Controls.Remove(cancelButton);cancelButton.Dispose();cancelButton=null;}
        void Error(Exception ex) {status=ex.Message;fraction=0;Invalidate();if(!preview&&!closing)MessageBox.Show(this,ex.Message,"EverBound",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        void OpenFolder(string path) {if(!Directory.Exists(path)){status="A pasta será criada ao instalar o EverBound.";Invalidate();return;}System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",Data.Quote(path)) {UseShellExecute=true});}
        void OpenLink(string link) {System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link){UseShellExecute=true});}

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);var g=e.Graphics;g.ScaleTransform(scale,scale);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.ClearTypeGridFit;
            using(var brush=new SolidBrush(Color.FromArgb(12,16,15)))g.FillRectangle(brush,0,0,208,760);
            using(var pen=new Pen(Color.FromArgb(62,65,51))) {g.DrawLine(pen,208,24,208,736);g.DrawRectangle(pen,1,1,1157,757);g.DrawLine(pen,232,75,1136,75);}
            using(var pen=new Pen(Gold,1.2f)) {
                g.DrawEllipse(pen,70,38,68,68);g.DrawEllipse(pen,77,45,54,54);g.DrawLine(pen,104,52,104,91);
                g.DrawLines(pen,new[]{new Point(84,65),new Point(104,79),new Point(124,65)});g.DrawLines(pen,new[]{new Point(91,56),new Point(104,69),new Point(117,56)});
                Diamond(g,104,103,5,Gold);
            }
            Draw(g,"EVERBOUND",new Font("Georgia",19,FontStyle.Regular,GraphicsUnit.Pixel),Cream,new Rectangle(23,127,166,32),true);
            Draw(g,"ELDEN RING · PROJETO MMO",new Font("Segoe UI",9,FontStyle.Regular,GraphicsUnit.Pixel),Muted,new Rectangle(22,165,167,21),true);
            Draw(g,page.ToUpperInvariant(),new Font("Segoe UI",12,FontStyle.Regular,GraphicsUnit.Pixel),Muted,new Rectangle(250,31,780,28),false);
            Draw(g,"LAUNCHER 1.0",new Font("Segoe UI",10,FontStyle.Regular,GraphicsUnit.Pixel),Muted,new Rectangle(26,728,160,20),false);
            Draw(g,"EVERBOUND  /  ELDEN RING 1.17",new Font("Segoe UI",10,FontStyle.Regular,GraphicsUnit.Pixel),Muted,new Rectangle(796,718,312,21),false);
            for(int i=0;i<navigation.Count;++i)if(navigation[i].Selected)using(var b=new SolidBrush(Gold))g.FillRectangle(b,13,240+i*58,3,19);
            if(page=="Início") {
                var dest=new Rectangle(232,90,904,356);
                g.DrawImage(art,dest,new Rectangle(0,0,art.Width,art.Height),GraphicsUnit.Pixel);
                using(var fade=new LinearGradientBrush(dest,Color.FromArgb(130,13,18,16),Color.FromArgb(0,13,18,16),0f))g.FillRectangle(fade,dest);
                using(var fade=new LinearGradientBrush(new Rectangle(232,340,904,106),Color.Transparent,Bg,90f))g.FillRectangle(fade,232,340,904,106);
                using(var p=new Pen(Color.FromArgb(69,69,48))) {g.DrawLine(p,250,455,1108,455);g.DrawLine(p,250,640,1108,640);}
                Diamond(g,1105,455,4,Gold);
            } else {
                using(var b=new SolidBrush(Color.FromArgb(9,12,11)))g.FillRectangle(b,232,90,904,550);
                using(var pen=new Pen(Color.FromArgb(50,56,44)))g.DrawRectangle(pen,232,90,904,550);
            }
            using(var bg=new SolidBrush(Color.FromArgb(39,44,36)))g.FillRectangle(bg,232,741,904,3);
            if(fraction>0)using(var fill=new SolidBrush(Gold))g.FillRectangle(fill,232,741,(float)(904*Math.Min(1,fraction)),3);
            Draw(g,status,new Font("Segoe UI",12,FontStyle.Regular,GraphicsUnit.Pixel),busy?Gold:Muted,new Rectangle(250,716,540,20),false);
        }
        static void Diamond(Graphics g,int x,int y,int size,Color color){using(var pen=new Pen(color))g.DrawPolygon(pen,new[]{new Point(x,y-size),new Point(x+size,y),new Point(x,y+size),new Point(x-size,y)});}
        static void Draw(Graphics g,string text,Font font,Color color,Rectangle rect,bool center) {using(font)using(var brush=new SolidBrush(color))using(var format=new StringFormat {Alignment=center?StringAlignment.Center:StringAlignment.Near,LineAlignment=StringAlignment.Near,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(text,font,brush,rect,format);}
        public void Render(string file,string view) {
            preview=true;status="Pacote verificado · EverBound 2.0.0";
            SetPage(view);CreateControl();PerformLayout();
            using(var bitmap=new Bitmap(ClientSize.Width,ClientSize.Height)) {
                DrawToBitmap(bitmap,ClientRectangle);
                // A hidden Form skips its children in WM_PRINT. Render the actual child controls explicitly.
                using(var graphics=Graphics.FromImage(bitmap))foreach(Control child in Controls) {
                    if(child.Width<1||child.Height<1)continue;
                    using(var image=new Bitmap(child.Width,child.Height)) {child.DrawToBitmap(image,new Rectangle(Point.Empty,child.Size));graphics.DrawImageUnscaled(image,child.Location);}
                }
                bitmap.Save(file,System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        protected override void Dispose(bool disposing) {if(disposing){timer.Dispose();if(art!=null)art.Dispose();}base.Dispose(disposing);}
    }

    static class Program {
        [STAThread] static void Main(string[] args) {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            var storage=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EverBound","Launcher");
            try {
                if(args.Length>=2&&args[0]=="--render") {
                    var image=Path.GetFullPath(args[1]);
                    // Offscreen rendering uses only an isolated workspace fixture; no user settings or game are changed.
                    using(var form=new MainForm(Path.Combine(Path.GetDirectoryName(image),"render-fixture"),true))form.Render(image,args.Length>2?args[2]:"Início");return;
                }
                if(args.Length==2&&args[0]=="--data-dir")storage=Path.GetFullPath(args[1]);
                bool first;
                using(var mutex=new Mutex(true,"Local\\EverBoundLauncher",out first)) {
                    if(!first){MessageBox.Show("O launcher EverBound já está aberto.","EverBound");return;}
                    Application.Run(new MainForm(storage,false));
                }
            } catch(Exception ex) {MessageBox.Show(ex.Message,"EverBound Launcher",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
    }
}
