using System;
using System.Collections.Generic;
using System.Threading;

namespace CommonLib;

/// <summary>
/// 鼠标输入缓冲和平滑器
/// 用于处理网络延迟和鼠标移动不连贯问题
/// </summary>
public class MouseInputBuffer
{
    private Queue<MouseDeltaFrame> deltaQueue = new Queue<MouseDeltaFrame>();
    private readonly object queueLock = new object();
    private const int BUFFER_SIZE = 8; // 缓冲帧数
    private const int SMOOTH_FRAMES = 3; // 平滑帧数（将一帧分散到3帧）
    private int smoothCounter = 0;
    private int smoothDeltaX = 0;
    private int smoothDeltaY = 0;

    public struct MouseDeltaFrame
    {
        public int DeltaX;
        public int DeltaY;
        public long Timestamp;
    }

    /// <summary>
    /// 添加鼠标增量到缓冲
    /// </summary>
    public void AddMouseDelta(int deltaX, int deltaY)
    {
        lock (queueLock)
        {
            // 如果缓冲已满，丢弃最旧的帧
            if (deltaQueue.Count >= BUFFER_SIZE)
            {
                deltaQueue.Dequeue();
            }

            deltaQueue.Enqueue(new MouseDeltaFrame
            {
                DeltaX = deltaX,
                DeltaY = deltaY,
                Timestamp = Environment.TickCount64
            });
        }
    }

    /// <summary>
    /// 获取平滑后的鼠标增量
    /// 每次调用返回平滑分散后的增量值
    /// </summary>
    public bool GetSmoothDelta(out int outDeltaX, out int outDeltaY)
    {
        outDeltaX = 0;
        outDeltaY = 0;

        lock (queueLock)
        {
            // 如果还在平滑当前帧
            if (smoothCounter > 0)
            {
                outDeltaX = smoothDeltaX / SMOOTH_FRAMES;
                outDeltaY = smoothDeltaY / SMOOTH_FRAMES;
                smoothCounter--;
                return true;
            }

            // 获取下一帧
            if (deltaQueue.Count > 0)
            {
                var frame = deltaQueue.Dequeue();
                smoothDeltaX = frame.DeltaX;
                smoothDeltaY = frame.DeltaY;
                smoothCounter = SMOOTH_FRAMES - 1;

                // 返回第一份
                outDeltaX = (frame.DeltaX + SMOOTH_FRAMES - 1) / SMOOTH_FRAMES;
                outDeltaY = (frame.DeltaY + SMOOTH_FRAMES - 1) / SMOOTH_FRAMES;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 获取当前缓冲大小
    /// </summary>
    public int GetBufferSize()
    {
        lock (queueLock)
        {
            return deltaQueue.Count;
        }
    }

    /// <summary>
    /// 清空缓冲
    /// </summary>
    public void Clear()
    {
        lock (queueLock)
        {
            deltaQueue.Clear();
            smoothCounter = 0;
        }
    }
}
