"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { startTransform } from "@/lib/api";

export default function Dashboard() {
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [clientId, setClientId] = useState("CLIENT_001");

  async function handleStart() {
    setLoading(true);
    setError(null);
    try {
      const result = await startTransform({
        client_id: clientId,
        mapping_path: `${clientId}/mapping/mapping.xlsm`,
        data_path: `${clientId}/client/transactions.xlsx`,
      });
      router.push(`/transform/${result.instance_id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to start");
      setLoading(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center">
      <div className="text-center">
        <h1 className="text-3xl font-bold text-gray-900 mb-4">
          Data Engineering Agent
        </h1>
        <p className="text-gray-600 mb-8">
          Transform source data to DNAV format with AI-assisted review
        </p>

        <div className="mb-6">
          <label
            htmlFor="clientId"
            className="block text-sm font-medium text-gray-700 mb-2"
          >
            Client ID
          </label>
          <input
            id="clientId"
            type="text"
            value={clientId}
            onChange={(e) => setClientId(e.target.value.trim())}
            className="w-64 px-4 py-2 border border-gray-300 rounded-lg text-center text-gray-900 focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
            placeholder="e.g. CLIENT_001"
          />
          <p className="mt-1 text-xs text-gray-500">
            Paths: mappings/{clientId}/mapping.xlsm, data/{clientId}/transactions.xlsx
          </p>
        </div>

        <button
          onClick={handleStart}
          disabled={loading || !clientId}
          className="px-8 py-4 bg-blue-600 text-white text-lg font-medium rounded-lg hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          {loading ? (
            <span className="flex items-center gap-2">
              <svg
                className="animate-spin h-5 w-5"
                xmlns="http://www.w3.org/2000/svg"
                fill="none"
                viewBox="0 0 24 24"
              >
                <circle
                  className="opacity-25"
                  cx="12"
                  cy="12"
                  r="10"
                  stroke="currentColor"
                  strokeWidth="4"
                />
                <path
                  className="opacity-75"
                  fill="currentColor"
                  d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
                />
              </svg>
              Starting...
            </span>
          ) : (
            "Start Transformation"
          )}
        </button>

        {error && (
          <p className="mt-4 text-red-600 text-sm">{error}</p>
        )}
      </div>
    </div>
  );
}
