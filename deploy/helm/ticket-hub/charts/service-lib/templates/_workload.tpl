{{/*
Renders everything one workload needs. Call with (dict "name" "seating" "svc" .Values.services.seating "root" $).
*/}}
{{- define "service-lib.workload" -}}
{{- $fullname := include "service-lib.fullname" . -}}
{{- $svc := .svc -}}
{{- $root := .root -}}
---
apiVersion: v1
kind: ConfigMap
metadata:
  name: {{ $fullname }}
  labels:
    {{- include "service-lib.labels" . | nindent 4 }}
data:
  {{- range $k, $v := $root.Values.global.env }}
  {{ $k }}: {{ $v | quote }}
  {{- end }}
  {{- range $k, $v := $svc.env }}
  {{ $k }}: {{ tpl (toString $v) $root | quote }}
  {{- end }}
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{ $fullname }}
  labels:
    {{- include "service-lib.labels" . | nindent 4 }}
spec:
  {{- if not (and $svc.hpa $svc.hpa.enabled) }}
  replicas: {{ $svc.replicas | default 1 }}
  {{- end }}
  revisionHistoryLimit: 3
  selector:
    matchLabels:
      {{- include "service-lib.selector" . | nindent 6 }}
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxUnavailable: 0
      maxSurge: 1
  template:
    metadata:
      labels:
        {{- include "service-lib.selector" . | nindent 8 }}
        {{- if $root.Values.workloadIdentity.enabled }}
        azure.workload.identity/use: "true"
        {{- end }}
      annotations:
        checksum/config: {{ toJson $svc.env | sha256sum | trunc 16 }}
    spec:
      serviceAccountName: {{ $root.Values.serviceAccount.name }}
      securityContext:
        runAsNonRoot: true
        seccompProfile:
          type: RuntimeDefault
      containers:
        - name: {{ .name }}
          image: {{ include "service-lib.image" . }}
          imagePullPolicy: {{ $root.Values.global.imagePullPolicy }}
          {{- with $svc.args }}
          args: {{ toJson . }}
          {{- end }}
          ports:
            - name: http
              containerPort: {{ $svc.port }}
            {{- range $svc.extraPorts }}
            - name: {{ .name }}
              containerPort: {{ .port }}
            {{- end }}
          envFrom:
            - configMapRef:
                name: {{ $fullname }}
          {{- if $svc.secretEnv }}
          env:
            {{- range $envName, $secretKey := $svc.secretEnv }}
            - name: {{ $envName }}
              valueFrom:
                secretKeyRef:
                  name: {{ $root.Values.secrets.name }}
                  key: {{ $secretKey }}
            {{- end }}
          {{- end }}
          {{- with $svc.probes }}
          startupProbe:
            httpGet: { path: {{ .liveness }}, port: http }
            failureThreshold: 30
            periodSeconds: 5
          livenessProbe:
            httpGet: { path: {{ .liveness }}, port: http }
            periodSeconds: 15
          readinessProbe:
            httpGet: { path: {{ .readiness }}, port: http }
            periodSeconds: 10
            failureThreshold: 3
          {{- end }}
          resources:
            {{- toYaml $svc.resources | nindent 12 }}
          securityContext:
            allowPrivilegeEscalation: false
            capabilities:
              drop: ["ALL"]
---
apiVersion: v1
kind: Service
metadata:
  name: {{ $fullname }}
  labels:
    {{- include "service-lib.labels" . | nindent 4 }}
spec:
  selector:
    {{- include "service-lib.selector" . | nindent 4 }}
  ports:
    - name: http
      port: {{ $svc.servicePort | default 80 }}
      targetPort: http
    {{- range $svc.extraPorts }}
    - name: {{ .name }}
      port: {{ .port }}
      targetPort: {{ .name }}
      {{- if .appProtocol }}
      appProtocol: {{ .appProtocol }}
      {{- end }}
    {{- end }}
{{- if $svc.pdb }}
---
apiVersion: policy/v1
kind: PodDisruptionBudget
metadata:
  name: {{ $fullname }}
  labels:
    {{- include "service-lib.labels" . | nindent 4 }}
spec:
  minAvailable: {{ $svc.pdb.minAvailable }}
  selector:
    matchLabels:
      {{- include "service-lib.selector" . | nindent 6 }}
{{- end }}
{{- if and $svc.hpa $svc.hpa.enabled }}
---
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: {{ $fullname }}
  labels:
    {{- include "service-lib.labels" . | nindent 4 }}
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: {{ $fullname }}
  minReplicas: {{ $svc.hpa.minReplicas }}
  maxReplicas: {{ $svc.hpa.maxReplicas }}
  metrics:
    - type: Resource
      resource:
        name: cpu
        target:
          type: Utilization
          averageUtilization: {{ $svc.hpa.cpu }}
  behavior:
    scaleDown:
      stabilizationWindowSeconds: 300
{{- end }}
{{- end -}}
