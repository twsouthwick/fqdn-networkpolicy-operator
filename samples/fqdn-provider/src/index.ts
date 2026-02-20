import express, { Request, Response } from "express";

const app = express();
const PORT = parseInt(process.env.PORT ?? "7942", 10);

interface PortRule {
  port: number;
  protocol: string;
}

interface EgressRule {
  addresses: string[];
  ports: PortRule[];
}

interface ProviderResponse {
  egress: EgressRule[];
}

// Edit these entries to configure which domains, IPs, and ports this provider exposes.
// Each entry maps a set of addresses to a set of ports.
const data: ProviderResponse = {
  egress: [
    {
      addresses: ["google.com", "10.10.10.10/32"],
      ports: [
        { port: 443, protocol: "TCP" },
        { port: 80, protocol: "TCP" },
      ],
    },
  ],
};

app.get("/addresses", (_req: Request, res: Response) => {
  res.json(data);
});

app.get("/healthz", (_req: Request, res: Response) => {
  res.json({ status: "ok" });
});

app.listen(PORT, () => {
  console.log(`fqdn-provider listening on port ${PORT}`);
});
