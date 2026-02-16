import { Router } from 'express';
import { sendVerification } from '../services/twilio';

export const authRoutes = Router();

const pendingCodes = new Map<string, { code: string; expires: number }>();

authRoutes.post('/verify', async (req, res) => {
  try {
    const { phone } = req.body;
    const code = Math.floor(100000 + Math.random() * 900000).toString();
    pendingCodes.set(phone, { code, expires: Date.now() + 600_000 });
    await sendVerification(phone, code);
    res.json({ success: true, message: 'Verification code sent' });
  } catch (err: any) {
    res.status(500).json({ error: err.message });
  }
});

authRoutes.post('/confirm', (req, res) => {
  const { phone, code } = req.body;
  const pending = pendingCodes.get(phone);
  if (!pending || pending.expires < Date.now()) {
    return res.status(400).json({ error: 'Code expired or not found' });
  }
  if (pending.code !== code) {
    return res.status(400).json({ error: 'Invalid code' });
  }
  pendingCodes.delete(phone);
  res.json({ success: true, verified: true });
});
