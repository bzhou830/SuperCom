using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace ComDev
{
    public partial class MainFrom : Form
    {
        private SerialPort comm = new SerialPort();
        private StringBuilder builder = new StringBuilder();            //避免在事件处理方法中反复的创建，定义到外面。
        private long receivedCount = 0;                                 //接收计数
        private long sendCount = 0;                                     //发送计数
        private bool Listening = false;                                 //是否没有执行完invoke相关操作
        private bool commClosing = false;                               //是否正在关闭串口，执行Application.DoEvents，并阻止再次invoke
        private List<byte> buffer = new List<byte>(4096);               //默认分配1页内存，并始终限制不允许超过
        private byte[] binary_data_1 = new byte[32 + MIN_COUNT];
        private short[] inputData = new short[8]; 

        private const int MIN_COUNT = 5;                                //接收最小数据包长度
        RevData rev;
        private bool timerOpened = false;

        // 波形图控件属性
        //private Color[] waveContrlColors;
        //private float waveFStyle;
        //private int[] waveIStyle;

        // 波形图中的数据
        public List<float>[] x = { new List<float>(), new List<float>(), new List<float>(), new List<float>() };
        public List<float>[] y = { new List<float>(), new List<float>(), new List<float>(), new List<float>() };

        public SentData sentData;

        public void initSendData()
        {
            sentData.addr       = 0x11;                 //0x11,                               // 地址
            sentData.funcCode   = 0x17;                 //0x17,                               // 功能码
            sentData.statAddr   = 0x0020;               //0x20, 0x00,                         // 读状态寄存器起始地址
            sentData.statNum    = 0x1000;               //0x00, 0x10,                         // 读状态寄存器数量
            sentData.cmdAddr    = 0x0030;               //0x30, 0x00,                         // 写命令寄存器起始地址
            sentData.cmdNum     = 0x0800;               //0x00, 0x08,                         // 写命令寄存器数量
            sentData.cmdBytes   = 0x10;                 //0x10,                               // 写命令寄存器字节数
        }


        public MainFrom()
        {
            InitializeComponent();
        }

        private void MainFrom_Load(object sender, EventArgs e)
        {
            //串口号
            string[] ports = SerialPort.GetPortNames();
            Array.Sort(ports);
            comboPortName.Items.AddRange(ports);
            comboPortName.SelectedIndex = comboPortName.Items.Count > 0 ? 0 : -1;

            //波特率
            string[] bauds = { "9600", "19200", "115200" };
            comboBaudrate.Items.AddRange(bauds);
            comboBaudrate.SelectedIndex = comboBaudrate.Items.IndexOf("115200");

            //数据位
            string[] dataBits = { "8", "7", "6" };
            comboDataBits.Items.AddRange(dataBits);
            comboDataBits.SelectedIndex = 0;

            //停止位
            string[] stopBits = { "1" };
            comboBoxStopBits.Items.AddRange(stopBits);
            comboBoxStopBits.SelectedIndex = 0;

            string[] PlotNames = {"St00", "St01", "St02", "St03",
                    "St04", "St05", "St06", "St07",
                    "St08", "St09", "St10", "St11",
                    "St12", "St13", "St14", "St15"};

            comboBoxBx1.Items.AddRange(PlotNames);
            comboBoxBx1.SelectedIndex = 0;

            comboBoxBx2.Items.AddRange(PlotNames);
            comboBoxBx2.SelectedIndex = 1;

            comboBoxBx3.Items.AddRange(PlotNames);
            comboBoxBx3.SelectedIndex = 2;

            comboBoxBx4.Items.AddRange(PlotNames);
            comboBoxBx4.SelectedIndex = 3;

            //初始化SerialPort对象
            comm.NewLine = "/r/n";
            comm.RtsEnable = true;          //根据实际情况
            
            //添加事件注册
            comm.DataReceived += commDataReceived;

            initSendData();
            buttonEnterInput_Click(sender, e);

            //初始化波形控件
            zGraph1.f_ClearAllPix();
            zGraph1.f_reXY();

            zGraph1.f_LoadOnePix(ref x[0], ref y[0], Color.Red, 2);
            zGraph1.f_AddPix(ref x[1], ref y[1], Color.Blue, 2);
            zGraph1.f_AddPix(ref x[2], ref y[2], Color.FromArgb(0, 128, 192), 2);
            zGraph1.f_AddPix(ref x[3], ref y[3], Color.Yellow, 2);

        }


        /// <summary>
        ///short数据的高8bit和低8bit互换
        /// </summary>
        /// <param name="src"></param>
        /// <returns></returns>
        public short highLowSwitch(int src)
        {
            src = (src >> 8) | (src & 0xff) << 8;
            return (short)src;
        }

        public short byte2Short(byte high, byte low)
        {
            int v = (high << 8) | low;
            return (short)v;
        }

        /// <summary>
        /// 生成要发送的数据报文，只填入数据，不带CRC
        /// </summary>
        public void genSendData()
        {
            sentData.content1 = inputData[0];
            sentData.content2 = inputData[1];
            sentData.content3 = inputData[2];
            sentData.content4 = inputData[3];
            sentData.content5 = inputData[4];
            sentData.content6 = inputData[5];
            sentData.content7 = inputData[6];
            sentData.content8 = inputData[7];
            //sentData.crcH = 0xaa;
            //sentData.crcL = 0x55;
        }


        void commDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            //如果正在关闭，忽略操作，直接返回，尽快的完成串口监听线程的一次循环
            if (commClosing)
                return;

            try
            {
                Listening = true;                   //设置标记，说明我已经开始处理数据，一会儿要使用系统UI的。
                int n = comm.BytesToRead;           //先记录下来，避免某种原因，人为的原因，操作几次之间时间长，缓存不一致
                byte[] buf = new byte[n];           //声明一个临时数组存储当前来的串口数据
                receivedCount += n;                 //增加接收计数
                comm.Read(buf, 0, n);               //读取缓冲数据

                //<协议解析>
                bool data_1_catched = false;        //缓存记录数据是否捕获到
                //1.缓存数据
                buffer.AddRange(buf);
                //2.完整性判断
                while (buffer.Count >= MIN_COUNT) //至少包含：地址（1）, 功能码（1）, 长度（1）, 校验（2）
                {
                    // 查找数据头, 地址, 功能码
                    if (buffer[0] == 0x11 && buffer[1] == 0x17)
                    {
                        //探测缓存数据是否有一条数据的字节，如果不够，就不用费劲的做其他验证了
                        //前面已经限定了剩余长度>=4，那我们这里一定能访问到buffer[2]这个长度
                        int len = buffer[2];                            // 有效的数据长度
                        
                        //异常码, 数据协议和原数据不同
                        if (len == 0x80)
                        {
                            len = 7;
                            MessageBox.Show("出现异常码");
                        }
                         
                        // 数据不够的时候支持跳出
                        if (buffer.Count < len + MIN_COUNT)
                            break;
                        
                        /*
                        // 校验数据，确认数据正确
                        byte checksum = 0;
                        for (int i = 0; i < len + 3; i++)               //len+3表示校验之前的位置
                        {
                            checksum ^= buffer[i];
                        }
                        if (checksum != buffer[len + 3])                //如果数据校验失败，丢弃这一包数据
                        {
                            buffer.RemoveRange(0, len + 4);             //从缓存中删除错误数据
                            continue;                                   //继续下一次循环
                        }
                        */

                        buffer.CopyTo(0, binary_data_1, 0, len + MIN_COUNT);    //复制一条完整数据到具体的数据缓存
                        data_1_catched = true;
                        buffer.RemoveRange(0, len + MIN_COUNT);                 //正确分析一条数据，从缓存中移除数据。
                    }
                    else
                    {
                        buffer.RemoveAt(0);
                    }
                }

                //分析数据
                if (data_1_catched)
                {
                    //数据都是定好格式的，所以当我们找到分析出的数据1，就知道固定位置一定是这些数据，我们只要显示就可以了
                    //string data = binary_data_1[3].ToString("X2") + " " + binary_data_1[4].ToString("X2") + " " +
                    //    binary_data_1[5].ToString("X2") + " " + binary_data_1[6].ToString("X2") + " " +
                    //    binary_data_1[7].ToString("X2");
                    string data = binary_data_1.ToString();

                    rev.content0 = byte2Short(binary_data_1[3], binary_data_1[4]);
                    rev.content1 = byte2Short(binary_data_1[5], binary_data_1[6]);
                    rev.content2 = byte2Short(binary_data_1[7], binary_data_1[8]);
                    rev.content3 = byte2Short(binary_data_1[9], binary_data_1[10]);
                    rev.content4 = byte2Short(binary_data_1[11], binary_data_1[12]);
                    rev.content5 = byte2Short(binary_data_1[13], binary_data_1[14]);
                    rev.content6 = byte2Short(binary_data_1[15], binary_data_1[16]);
                    rev.content7 = byte2Short(binary_data_1[17], binary_data_1[18]);
                    rev.content8 = byte2Short(binary_data_1[19], binary_data_1[20]);
                    rev.content9 = byte2Short(binary_data_1[21], binary_data_1[22]);
                    rev.content10 = byte2Short(binary_data_1[23], binary_data_1[24]);
                    rev.content11 = byte2Short(binary_data_1[25], binary_data_1[26]);
                    rev.content12 = byte2Short(binary_data_1[27], binary_data_1[28]);
                    rev.content13 = byte2Short(binary_data_1[29], binary_data_1[30]);
                    rev.content14 = byte2Short(binary_data_1[31], binary_data_1[32]);
                    rev.content15 = byte2Short(binary_data_1[33], binary_data_1[34]);

                    //更新界面
                    this.Invoke((EventHandler)(delegate
                    {
                        // 接受数据框数据更新
                        txData.Text = data;

                        //状态寄存器数据更新
                        textBoxStat00.Text = rev.content0.ToString("X2");
                        textBoxStat01.Text = rev.content1.ToString("X2");
                        textBoxStat02.Text = rev.content2.ToString("X2");
                        textBoxStat03.Text = rev.content3.ToString("X2");
                        textBoxStat04.Text = rev.content4.ToString("X2");
                        textBoxStat05.Text = rev.content5.ToString("X2");
                        textBoxStat06.Text = rev.content6.ToString("X2");
                        textBoxStat07.Text = rev.content7.ToString("X2");
                        textBoxStat08.Text = rev.content8.ToString("X2");
                        textBoxStat09.Text = rev.content9.ToString("X2");
                        textBoxStat10.Text = rev.content10.ToString("X2");
                        textBoxStat11.Text = rev.content11.ToString("X2");
                        textBoxStat12.Text = rev.content12.ToString("X2");
                        textBoxStat13.Text = rev.content13.ToString("X2");
                        textBoxStat14.Text = rev.content14.ToString("X2");
                        textBoxStat15.Text = rev.content15.ToString("X2");

                        //添加到曲线， 数据源选项来源于4个combobox
                        addDataToWave(comboBoxBx1.Items.IndexOf(comboBoxBx1.Text), 0);
                        addDataToWave(comboBoxBx2.Items.IndexOf(comboBoxBx2.Text), 1);
                        addDataToWave(comboBoxBx3.Items.IndexOf(comboBoxBx3.Text), 2);
                        addDataToWave(comboBoxBx4.Items.IndexOf(comboBoxBx4.Text), 3);

                        zGraph1.f_Refresh();
                    }));
                }
            }
            finally
            {
                Listening = false; //用完了，ui可以关闭串口
            }
        }

        private void buttonOpenClose_Click(object sender, EventArgs e)
        {
            //根据当前串口对象，来判断操作
            if (comm.IsOpen)
            {
                commClosing = true;
                while (Listening)
                    Application.DoEvents();
                comm.Close();     //打开时点击，则关闭串口
            }
            else
            {
                //关闭时点击，则设置好端口，波特率后打开
                comm.PortName = comboPortName.Text;
                comm.BaudRate = int.Parse(comboBaudrate.Text);
                comm.DataBits = int.Parse(comboDataBits.Text);
                try
                {
                    comm.Open();
                }
                catch (Exception ex)
                {
                    //捕获到异常信息，创建一个新的comm对象，之前的不能用了。
                    comm = new SerialPort();
                    //现实异常信息给客户。
                    MessageBox.Show(ex.Message);
                }
            }
            //设置按钮的状态
            buttonOpenClose.Text = comm.IsOpen ? "关闭" : "打开";
            buttonSend.Enabled = comm.IsOpen;
        }

        //动态的修改获取文本框是否支持自动换行。
        private void checkBoxNewlineGet_CheckedChanged(object sender, EventArgs e)
        {
            txGet.WordWrap = checkBoxNewlineGet.Checked;
        }


        private void buttonSend_Click(object sender, EventArgs e)
        {
            //定义一个变量，记录发送了几个字节
            int n = 0;
            //16进制发送
            if (checkBoxHexSend.Checked)
            {
                //我们不管规则了。如果写错了一些，我们允许的，只用正则得到有效的十六进制数
                MatchCollection mc = Regex.Matches(txSend.Text, @"(?i)[/da-f]{2}");
                List<byte> buf = new List<byte>();//填充到这个临时列表中
                //依次添加到列表中
                foreach (Match m in mc)
                {
                    buf.Add(byte.Parse(m.Value, System.Globalization.NumberStyles.HexNumber));
                }
                //转换列表为数组后发送
                comm.Write(buf.ToArray(), 0, buf.Count);
                //记录发送的字节数
                n = buf.Count;
            }
            else//ascii编码直接发送
            {
                //包含换行符
                if (checkBoxNewlineSend.Checked)
                {
                    comm.WriteLine(txSend.Text);
                    n = txSend.Text.Length + 2;
                }
                else//不包含换行符
                {
                    comm.Write(txSend.Text);
                    n = txSend.Text.Length;
                }
            }
            sendCount += n;//累加发送字节数
            labelSendCount.Text = "Send:" + sendCount.ToString();//更新界面
        }


        private void buttonReset_Click(object sender, EventArgs e)
        {
            genSendData();
            byte[] d   = structTransform.StructToBytes(sentData);
            
            byte[] CRC = ComDev.CRC.ModbusCrc16Calc(d);
            d[d.Length - 2] = CRC[0];
            d[d.Length - 1] = CRC[1];
            if (comm.IsOpen)
            {
                comm.Write(d, 0, 13 + 16);
            }
        }

        private void buttonTimer_Click(object sender, EventArgs e)
        {
            if (!timerOpened)
            {
                try
                {
                    timer1.Interval = int.Parse(textBoxTimeVal.Text);
                }
                catch(Exception ex)
                {
                    MessageBox.Show("定时发送开启失败，因为数据设置错误！" + ex.ToString());
                    return;
                }

                timer1.Start();
                buttonTimer.Text = "关闭发送";
                timerOpened = true;
            }
            else
            {
                timer1.Stop();
                buttonTimer.Text = "定时发送";
                timerOpened = false;
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            genSendData();
            byte[] d = structTransform.StructToBytes(sentData);
            byte[] CRC = ComDev.CRC.ModbusCrc16Calc(d);
            d[d.Length - 2] = CRC[0];
            d[d.Length - 1] = CRC[1];

            if (comm.IsOpen){
                comm.Write(d, 0, 13 + 16);
            }
        }

        /// <summary>
        /// 确认输入的内容
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonEnterInput_Click(object sender, EventArgs e)
        {
            try
            {
                inputData[0] = highLowSwitch(utils.StringToHexOrDec(textBoxCmd0.Text));
                inputData[1] = highLowSwitch(utils.StringToHexOrDec(textBoxCmd1.Text));
                inputData[2] = highLowSwitch(utils.StringToHexOrDec(textBoxCmd2.Text));
                inputData[3] = highLowSwitch(utils.StringToHexOrDec(textBoxCmd3.Text));
                inputData[4] = highLowSwitch(utils.StringToHexOrDec(textBoxCmd4.Text));
                inputData[5] = highLowSwitch(utils.StringToHexOrDec(textBoxCmd5.Text));
                inputData[6] = highLowSwitch(utils.StringToHexOrDec(textBoxCmd6.Text));
                inputData[7] = highLowSwitch(utils.StringToHexOrDec(textBoxCmd7.Text));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        /// <summary>
        /// 添加数据到波形显示
        /// </summary>
        /// <param name="sourceId">数据源</param>
        /// <param name="waveId">波形号</param>
        private void addDataToWave(int sourceId, int waveId)
        {
            switch (sourceId)
            {
                case 0: x[waveId].Add((float)(x[waveId].Count + 1)); y[waveId].Add((float)((float)rev.content0)); break;
                case 1: x[waveId].Add((float)(x[waveId].Count + 1)); y[waveId].Add((float)((float)rev.content1)); break;
                case 2: x[waveId].Add((float)(x[waveId].Count + 1)); y[waveId].Add((float)((float)rev.content2)); break;
                case 3: x[waveId].Add((float)(x[waveId].Count + 1)); y[waveId].Add((float)((float)rev.content3)); break;
                case 4: x[waveId].Add((float)(x[waveId].Count + 1)); y[waveId].Add((float)((float)rev.content4)); break;
                case 5: x[waveId].Add((float)(x[waveId].Count + 1)); y[waveId].Add((float)((float)rev.content5)); break;
                case 6: x[waveId].Add((float)(x[waveId].Count + 1)); y[waveId].Add((float)((float)rev.content6)); break;
                case 7: x[waveId].Add((float)(x[waveId].Count + 1)); y[waveId].Add((float)((float)rev.content7)); break;
                case 8: x[waveId].Add((float)(x[waveId].Count + 1)); y[waveId].Add((float)((float)rev.content8)); break;
                case 9: x[waveId].Add((float)(x[waveId].Count + 1)); y[waveId].Add((float)((float)rev.content9)); break;
                case 10: x[waveId].Add((float)(x[waveId].Count + 1)); y[waveId].Add((float)((float)rev.content10)); break;
                case 11: x[waveId].Add((float)(x[waveId].Count + 1)); y[waveId].Add((float)((float)rev.content11)); break;
                case 12: x[waveId].Add((float)(x[waveId].Count + 1)); y[waveId].Add((float)((float)rev.content12)); break;
                case 13: x[waveId].Add((float)(x[waveId].Count + 1)); y[waveId].Add((float)((float)rev.content13)); break;
                case 14: x[waveId].Add((float)(x[waveId].Count + 1)); y[waveId].Add((float)((float)rev.content14)); break;
                case 15: x[waveId].Add((float)(x[waveId].Count + 1)); y[waveId].Add((float)((float)rev.content15)); break;
            }
        }

        // plot index changed, clear data.
        private void comboBoxPlot_SelectedIndexChanged(object sender, EventArgs e)
        {
            clearWaveData();
        }

        private void clearWaveData()
        {
            for(int i = 0; i < 4; i++) {
                x[i].Clear();
            }
        }
    }
}
