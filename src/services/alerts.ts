import { sendSMS, makeVoiceCall } from './twilio';

export interface TradingSignal {
  symbol: string;
  action: 'BUY' | 'SELL' | 'HOLD';
  price: number;
  confidence: number;
  indicators: string[];
  timestamp: number;
}

export async function sendTradeAlert(phone: string, signal: TradingSignal): Promise<void> {
  const emoji = signal.action === 'BUY' ? '🟢' : signal.action === 'SELL' ? '🔴' : '🟡';
  const msg = `${emoji} ${signal.action} ${signal.symbol}\nPrice: $${signal.price.toFixed(2)}\nConfidence: ${(signal.confidence * 100).toFixed(0)}%\nSignals: ${signal.indicators.join(', ')}`;
  await sendSMS(phone, msg);
}

export async function sendUrgentVoiceAlert(phone: string, signal: TradingSignal): Promise<void> {
  const twiml = `<Response><Say voice="alice">Trading alert. ${signal.action} signal for ${signal.symbol} at ${signal.price.toFixed(2)} dollars. Confidence level ${(signal.confidence * 100).toFixed(0)} percent.</Say></Response>`;
  await makeVoiceCall(phone, twiml);
}

export function shouldAlertByVoice(signal: TradingSignal): boolean {
  // Voice alerts for high-confidence signals on major moves
  return signal.confidence > 0.85 && Math.abs(signal.price) > 1000;
}
