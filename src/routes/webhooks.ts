import { Router } from 'express';
import { sendTradeAlert, sendUrgentVoiceAlert, shouldAlertByVoice, TradingSignal } from '../services/alerts';

export const webhookRoutes = Router();

// Registered subscribers (would be from DB in production)
const subscribers = new Map<string, string[]>(); // symbol -> phone numbers

webhookRoutes.post('/signal', async (req, res) => {
  try {
    const signal: TradingSignal = req.body;
    const phones = subscribers.get(signal.symbol) || [];
    
    const results = await Promise.allSettled(
      phones.map(async (phone) => {
        if (shouldAlertByVoice(signal)) {
          await sendUrgentVoiceAlert(phone, signal);
        }
        await sendTradeAlert(phone, signal);
      })
    );

    const sent = results.filter(r => r.status === 'fulfilled').length;
    res.json({ success: true, sent, total: phones.length });
  } catch (err: any) {
    res.status(500).json({ error: err.message });
  }
});
