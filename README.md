# Trading Alerts API 📈

A real-time trading signal notification system built with Node.js, Express, and Twilio.

## Features

- **SMS Alerts** — Instant trading signal notifications via Twilio SMS
- **Voice Callbacks** — Automated voice alerts for critical price movements
- **2FA Verification** — Phone-based two-factor authentication for user accounts
- **Webhook Integration** — Connect to any trading platform via REST webhooks
- **Rate Limiting** — Smart alert throttling to prevent notification spam

## Tech Stack

- Node.js / TypeScript
- Express.js
- Twilio (SMS + Voice)
- Redis (rate limiting + caching)
- PostgreSQL (user accounts + alert history)

## Quick Start

```bash
npm install
cp .env.example .env
# Add your Twilio credentials to .env
npm run dev
```

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/alerts/sms` | Send SMS alert |
| POST | `/api/alerts/voice` | Trigger voice callback |
| POST | `/api/auth/verify` | Send 2FA verification code |
| POST | `/api/auth/confirm` | Confirm 2FA code |
| POST | `/api/webhooks/signal` | Receive trading signal webhook |
| GET | `/api/alerts/history` | Get alert history |

## Environment Variables

```
TWILIO_ACCOUNT_SID=your_sid
TWILIO_AUTH_TOKEN=your_token
TWILIO_PHONE_NUMBER=+1234567890
DATABASE_URL=postgresql://...
REDIS_URL=redis://...
JWT_SECRET=your_secret
```

## License

MIT
