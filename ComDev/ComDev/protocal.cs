using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;


namespace ComDev
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    [Serializable()]
    public struct SentData
    {
        public byte addr;           //0x11,                               // 地址
        public byte funcCode;       //0x17,                               // 功能码
        public short statAddr;       //0x20, 0x00,                         // 读状态寄存器起始地址
        public short statNum;        //0x00, 0x10,                         // 读状态寄存器数量
        public short cmdAddr;        //0x30, 0x00,                         // 写命令寄存器起始地址
        public short cmdNum;         //0x00, 0x08,                         // 写命令寄存器数量
        public byte cmdBytes;        //0x10,                               // 写命令寄存器字节数

        // 写命令寄存器内容
        public short content1;       //0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        public short content2;       //0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        public short content3;
        public short content4;
        public short content5;
        public short content6;
        public short content7;
        public short content8;

        // CRCH, CRCL
        public byte crcH; //0x00, 0x00};
        public byte crcL;
    };


    public struct RevData
    {
        public byte addr;           //0x11,                               // 地址
        public byte funcCode;       //0x17,                               // 功能码
        public byte statBytes;      //0x20,                               // 读取状态寄存器字节数

        // 状态寄存器内容
        public short content0;
        public short content1;       
        public short content2;       
        public short content3;
        public short content4;
        public short content5;
        public short content6;
        public short content7;
        public short content8;
        public short content9;       
        public short content10;         
        public short content11;
        public short content12;
        public short content13;
        public short content14;
        public short content15;


        // CRCH, CRCL
        public byte crcH; //0x00, 0x00};
        public byte crcL;
    };

}
