import type {
  CampaignDetail,
  CampaignEvent,
  CampaignStateResponse,
  CampaignSummary,
  CampaignTurnSummary,
  GameClient,
  ScenarioContent,
  Side,
  SubmitCommand,
  TheatreSummary
} from "./types";

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(init?.headers ?? {})
    }
  });

  if (!response.ok) {
    const fallback = `${response.status} ${response.statusText}`;
    try {
      const problem = (await response.json()) as { title?: string; detail?: string; errors?: Record<string, string[]> };
      const validation = problem.errors
        ? Object.values(problem.errors).flat().join(" ")
        : undefined;
      throw new Error(problem.detail ?? validation ?? problem.title ?? fallback);
    } catch (error) {
      if (error instanceof Error && error.message !== fallback) {
        throw error;
      }
      throw new Error(fallback);
    }
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export class HttpGameClient implements GameClient {
  health() {
    return request<{ status: string; service: string }>("/api/health");
  }

  loadTheatres() {
    return request<TheatreSummary[]>("/api/content/theatres");
  }

  loadScenarioContent(theatreId: string, scenarioId: string) {
    return request<ScenarioContent>(`/api/content/theatres/${theatreId}/scenarios/${scenarioId}`);
  }

  listCampaigns() {
    return request<CampaignSummary[]>("/api/campaigns");
  }

  createCampaign(input: { name?: string; scenarioId?: string; playerSide?: Side; randomSeed?: number }) {
    return request<CampaignDetail>("/api/campaigns", {
      method: "POST",
      body: JSON.stringify(input)
    });
  }

  loadCampaignState(campaignUid: string) {
    return request<CampaignStateResponse>(`/api/campaigns/${campaignUid}/state`);
  }

  loadEvents(campaignUid: string) {
    return request<CampaignEvent[]>(`/api/campaigns/${campaignUid}/events?order=LatestTurnFirst&limit=60`);
  }

  loadTurns(campaignUid: string) {
    return request<CampaignTurnSummary[]>(`/api/campaigns/${campaignUid}/turns?limit=30`);
  }

  async submitCommands(campaignUid: string, commands: SubmitCommand[]) {
    await request(`/api/campaigns/${campaignUid}/commands`, {
      method: "POST",
      body: JSON.stringify({ commands })
    });
  }

  async resolveTurn(campaignUid: string) {
    await request(`/api/campaigns/${campaignUid}/resolve-turn`, {
      method: "POST"
    });
  }

  async chooseTensionOption(campaignUid: string, cardId: string, optionId: string) {
    await request(`/api/campaigns/${campaignUid}/tensions/${cardId}/choose`, {
      method: "POST",
      body: JSON.stringify({ optionId })
    });
  }
}
