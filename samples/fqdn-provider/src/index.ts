import express, { Request, Response } from "express";

const app = express();
const PORT = parseInt(process.env.PORT ?? "7942", 10);

interface ProviderResponse {
  domains: string[];
  ips: string[];
}

// Edit these lists to configure which domains and IPs this provider exposes.
const data: ProviderResponse = {
  domains: ["google.com"],
  ips: ["10.10.10.10/32"],
};

app.get("/fqdnList", (_req: Request, res: Response) => {
  res.json(data);
});

app.get("/healthz", (_req: Request, res: Response) => {
  res.json({ status: "ok" });
});

app.listen(PORT, () => {
  console.log(`fqdn-provider listening on port ${PORT}`);
});
