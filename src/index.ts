import express from 'express';
import cors from 'cors';
import helmet from 'helmet';
import dotenv from 'dotenv';
import { alertRoutes } from './routes/alerts';
import { authRoutes } from './routes/auth';
import { webhookRoutes } from './routes/webhooks';
import { rateLimiter } from './middleware/rate-limit';

dotenv.config();

const app = express();
const PORT = process.env.PORT || 3000;

app.use(helmet());
app.use(cors());
app.use(express.json());
app.use(rateLimiter);

app.use('/api/alerts', alertRoutes);
app.use('/api/auth', authRoutes);
app.use('/api/webhooks', webhookRoutes);

app.get('/health', (req, res) => {
  res.json({ status: 'ok', uptime: process.uptime() });
});

app.listen(PORT, () => {
  console.log(`Trading Alerts API running on port ${PORT}`);
});
