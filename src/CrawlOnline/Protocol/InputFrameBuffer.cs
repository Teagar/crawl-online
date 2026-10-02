namespace CrawlOnline.Protocol
{
    public sealed class InputFrameBuffer
    {
        private InputFrame latest;
        private byte pendingDown;
        private byte pendingUp;
        private int downPresentedFrame = -1;
        private int upPresentedFrame = -1;

        public InputFrame Latest { get { return latest; } }

        public void Set(InputFrame frame, int currentFrame)
        {
            if (downPresentedFrame >= 0 && downPresentedFrame != currentFrame)
            {
                pendingDown = 0;
                downPresentedFrame = -1;
            }
            if (upPresentedFrame >= 0 && upPresentedFrame != currentFrame)
            {
                pendingUp = 0;
                upPresentedFrame = -1;
            }
            latest = frame;
            pendingDown |= frame.DownButtons;
            pendingUp |= frame.UpButtons;
        }

        public byte GetDown(int currentFrame)
        {
            return Present(ref pendingDown, ref downPresentedFrame, currentFrame);
        }

        public byte GetUp(int currentFrame)
        {
            return Present(ref pendingUp, ref upPresentedFrame, currentFrame);
        }

        private static byte Present(ref byte pending, ref int presentedFrame, int currentFrame)
        {
            if (presentedFrame >= 0 && presentedFrame != currentFrame)
            {
                pending = 0;
                presentedFrame = -1;
            }
            if (presentedFrame < 0) presentedFrame = currentFrame;
            return pending;
        }
    }
}
