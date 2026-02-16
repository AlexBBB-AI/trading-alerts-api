import { Router } from 'express';
import { sendTradeAlert, sendUrgentVoiceAlert } from '../services/alerts';

export const alertRoutes = Router();

alertRoutes.post('/sms', async (req, res) => {
  try {
    const { phone, signal } = req.body;
    await sendTradeAlert(phone, signal);
    res.json({ success: true, message: 'SMS alert sent' });
  } catch (err: any) {
    res.status(500).json({ error: err.message });
  }
});

alertRoutes.post('/voice', async (req, res) => {
  try {
    const { phone, signal } = req.body;
    await sendUrgentVoiceAlert(phone, signal);
    res.json({ success: true, message: 'Voice alert initiated' });
  } catch (err: any) {
    res.status(500).json({ error: err.message });
  }
});

alertRoutes.get('/history', async (req, res) => {
  // TODO: Implement alert history from database
  res.json({ alerts: [], total: 0 });
});
