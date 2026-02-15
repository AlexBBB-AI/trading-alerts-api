import twilio from 'twilio';

const client = twilio(
  process.env.TWILIO_ACCOUNT_SID,
  process.env.TWILIO_AUTH_TOKEN
);

const FROM = process.env.TWILIO_PHONE_NUMBER!;

export async function sendSMS(to: string, message: string): Promise<string> {
  const msg = await client.messages.create({
    body: message,
    from: FROM,
    to,
  });
  return msg.sid;
}

export async function makeVoiceCall(to: string, twiml: string): Promise<string> {
  const call = await client.calls.create({
    twiml,
    from: FROM,
    to,
  });
  return call.sid;
}

export async function sendVerification(to: string, code: string): Promise<string> {
  return sendSMS(to, `Your verification code is: ${code}. Valid for 10 minutes.`);
}
