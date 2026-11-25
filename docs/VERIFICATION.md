# Báo cáo triển khai và kiểm chứng

## Bốn trạng thái request 0.0.1 — 25/11/2026

- Đổi bộ trạng thái global của request thành `new` (Mới), `uploading` (Đang gửi ảnh), `done` (Hoàn tất), `error` (Lỗi); `review` thay bằng `error` trong service, bộ lọc danh sách và nhãn Desktop.
- Giữ nguyên nghiệp vụ: upload lỗi chắc chắn (401/403/404/413/415/429 hoặc không gửi được) vẫn trả về `new` để sửa và gửi lại; kết quả không chắc chắn vẫn chặn gửi lại và xóa cho tới khi người dùng xác nhận, giờ dưới mã `error`.
- Request cũ lưu `review` được đọc thành `error` trong bộ nhớ, không ghi lại file JSON lúc khởi động; lần lưu kế tiếp mới ghi `error`. Có unit test riêng cho đường đọc này.
- Đổi giá trị trạng thái trả cho web qua API local (`review` → `error`); web cần so khớp `error` thay vì `review`.
- `dotnet test DicomAgent.sln -c Release`: 45 unit + 63 integration pass, build Release 0 warning/0 error.
- PASS: `bash deploy/release.sh` tự tăng 0.2.15 → 0.0.1, tạo `DicomAgent-0.0.1-win-x64-Setup.exe` và `dicom-agent_0.0.1_amd64.deb`, đối chiếu payload/md5sums và cập nhật `SHA256SUMS`, exit 0.
- NOT RUN: cài/nâng cấp thật trên Windows/Linux trong phiên này.

## Việt hóa trạng thái và sửa lỗi release 0.2.15 — 25/11/2026

- Tái hiện lỗi release 0.2.14: 43 unit PASS, integration có 1 FAIL tại `DesktopTests.HeadlessWindowLoadsRequestsCaptureSettingsAndStatusBinding`, do test tìm `Running` sau khi giao diện đã đổi thành `Đang chạy`; test màu trạng thái cũng còn tìm `new`.
- Cập nhật test theo nhãn tiếng Việt và giữ kiểm tra màu sáng/tối. Headless test kiểm tra cả danh sách/chi tiết ca cho bốn trạng thái, nhãn bộ lọc và mã trạng thái gốc không đổi. Test converter kiểm tra trạng thái dịch vụ, trạng thái không xác định và thông báo kết quả gửi ảnh có mã HTTP/hướng dẫn xử lý.
- Desktop chỉ dịch phần hiển thị; trạng thái API/JSON, bộ lọc theo mã, màu trạng thái, quy tắc nhận ảnh và gửi ảnh giữ nguyên. Thông báo chẩn đoán chưa có bản dịch được giữ nguyên để không suy diễn sai lỗi.
- PASS: chạy đúng `bash /home/duynguyen/Dev/DicomAgentPlugin/deploy/release.sh`, SDK 8.0.422, build Release 0 warning/0 error, 43 unit + 63 integration test. Script tự tăng version từ 0.2.14 lên 0.2.15 và tiếp tục đóng gói sau test.
- PASS: release hoàn tất exit 0; publish mới Windows/Linux, tạo `DicomAgent-0.2.15-win-x64-Setup.exe` và `dicom-agent_0.2.15_amd64.deb`, trích xuất đối chiếu payload, kiểm tra Debian md5sums và cập nhật `publish/installers/SHA256SUMS`.
- NOT RUN: cài/nâng cấp và thao tác Desktop native trên Windows/Linux trong phiên này.

## Điều kiện dừng nhận cho ca 0.2.13 — 25/11/2026

- Tab Thực hiện dịch vụ chỉ bật **Dừng nhận cho ca** khi ca đang xem có trạng thái `new`, trùng ca đang nhận trong snapshot Host và Desktop không đang xử lý thao tác. Command cũng kiểm tra điều kiện trước khi gửi `capture-stop`.
- Test hồi quy qua HTTP localhost: chưa chọn/chưa nhận thì khóa; bật nhận ca A thì cho dừng; xem ca B không dừng được A; khóa khi đang dừng; sau dừng thì khóa; cập nhật đúng khi Host chuyển ca hoặc xóa ca đang xem. Headless test kiểm tra binding nút bị khóa khi ca chưa bật nhận.
- PASS: SDK 8.0.422, build Release 0 warning/0 error, 43 unit + 44 integration test. Test chạy ngoài sandbox để dùng TCP loopback.
- PASS: bộ cài Windows/Linux được publish mới từ source 0.2.13; trích xuất đối chiếu payload với publish và kiểm tra Debian md5sums thành công, SHA-256 cập nhật tại `publish/installers/SHA256SUMS`.
- NOT RUN: cài/nâng cấp và thao tác Desktop native trên Windows/Linux trong phiên này.

## Cập nhật Windows 0.1.1 — 10/11/2026

- Publish Windows self-contained cho Host, Desktop và SCU thành công. Setup đóng kèm SCU; script cài vào `%SystemDrive%\DicomSimulator`, giữ nguyên dữ liệu/configuration hiện hữu.
- Desktop dùng Windows GUI subsystem (kiểm tra PE: subsystem = 2), không tạo console khi khởi chạy ứng dụng.
- Thêm icon và menu tray Windows: Open khôi phục cửa sổ; X ẩn cửa sổ; Exit desktop thoát UI, không dừng service. Đường đóng do OS/app shutdown được phép đóng bình thường.
- Build Release: 0 warnings, 0 errors. Regression tests Linux: 24 unit + 19 integration PASS. Headless test kiểm tra tài nguyên icon, đóng ẩn không hủy cửa sổ, khôi phục cửa sổ từ trạng thái minimized và đóng thật khi bỏ chế độ tray.
- PowerShell parser và Bash syntax PASS. Cài/nâng cấp/ACL và thao tác tray thực tế trên Windows: NOT RUN trong phiên phát triển này; cần kiểm tra trên Windows VM. Vị trí icon trong nhóm hidden icons do Windows quản lý.
- Setup 0.1.1 đã build: `publish/installers/DicomAgent-0.1.1-win-x64-Setup.exe` (57.281.419 bytes). Kiểm tra archive và extract bằng 7-Zip PASS; SHA-256 của 417 file payload Host/Desktop/SCU khớp publish. SHA-256 installer: `fd91ee903eab787fe969089beb9249fa45a0f65c72d88a30b60d703ea3bf34b9`, được thêm vào `publish/installers/SHA256SUMS`.

Kiểm tra trên Windows sau khi cài bản mới: SCU tồn tại; mở Start Menu không có terminal; X làm mất cửa sổ khỏi taskbar nhưng icon tray còn; Open mở lại; Exit desktop kết thúc UI; C-ECHO/C-STORE vẫn thành công nhờ service; ảnh cũ/settings vẫn giữ sau nâng cấp. Thoát UI qua menu tray trước nâng cấp tiếp theo.

## Báo cáo gốc 0.1.0

Ngày: 09/11/2026, môi trường Linux Ubuntu 26.04 x64; SDK 8.0.422 / runtime 8.0.28. Source là .NET 8/C# 12. Repository ban đầu trống, không có source hay dữ liệu khách hàng để thay đổi.

## Projects và kiến trúc

Tạo tám project trong `DicomAgent.sln`:

| Project | Trách nhiệm |
|---|---|
| DicomAgent.Domain | Entity metadata, không dependency framework |
| DicomAgent.Application | DTO/contracts, validation, path policy, IPC client |
| DicomAgent.Infrastructure | fo-dicom SCP, file store, EF/SQLite, migration/reconciliation, IPC server |
| DicomAgent.Host | Worker, DI, config, Windows Service/systemd lifecycle |
| DicomAgent.Desktop | Avalonia MVVM, dashboard, settings, history, browse/copy/open location |
| DicomAgent.UnitTests | Validation/config/UID/path/hash protocol bounds |
| DicomAgent.IntegrationTests | DICOM networking, storage/DB failure, IPC, Host process, headless UI |
| DicomAgent.Scu | CLI C-ECHO/C-STORE và tạo DX fixture tổng hợp |

Domain ← Application ← Infrastructure; Host tham chiếu Application/Infrastructure; Desktop chỉ tham chiếu Application. Test project có thể tham chiếu nhiều adapter để kiểm chứng tích hợp. Không upload website, không cloud dependency runtime.

## Kết quả

| Kiểm chứng | Kết quả | Bằng chứng/phạm vi |
|---|---|---|
| Restore package .NET 8 | PASS | Package/API thực tế; fo-dicom 5.2.6, EF Core 8.0.22, Avalonia 11.3.7 |
| Release solution build | PASS | 0 warnings, 0 errors |
| Unit tests | PASS | 24/24 |
| Integration tests trên Linux | PASS | 19/19, tổng cộng 43 test pass, 0 fail, 0 skipped trong lượt Linux |
| C-ECHO và C-STORE | PASS | SCU/SCP fo-dicom qua TCP loopback thật |
| Implicit/Explicit VR Little Endian | PASS | Transfer Syntax đọc lại đúng, Pixel Data 2×2 và PatientName tổng hợp được bảo toàn |
| Metadata/history/hash | PASS | SQLite migration, một record/file, SHA-256 và size khớp |
| Duplicate/cùng UID khác nội dung | PASS | Không overwrite, không thêm record; retransmit từ Calling AE khác cũng nhận dạng duplicate |
| Concurrent C-STORE | PASS | 6 request đồng thời, gồm UID trùng và UID khác |
| Missing UID/optional tags | PASS | Thiếu required bị từ chối, optional null được xử lý |
| Called AE / Calling AE / IP allowlist | PASS | Association không được phép bị từ chối |
| Port bị chiếm | PASS | Fail fast, không vòng lặp restart CPU spike |
| File write failure/size limit | PASS | Trả failure hoặc abort, không công bố file hoàn chỉnh/history |
| SQLite failure | PASS | Test repository lỗi và table SQLite bị drop: file giữ lại; mô phỏng phục hồi repository + reconcile hai lượt không duplicate |
| IPC quản trị | PASS | Status/settings/save/history, reject operation không hợp lệ, reject cấu hình sai, Unix socket không có quyền other |
| Đóng kết nối quản trị | PASS | C-ECHO/C-STORE tiếp tục, Host độc lập client |
| Host restart và persistent config | PASS | Chạy tiến trình Host thật, lưu AE mới, SIGTERM exit 0, restart dùng AE mới và history cũ |
| Avalonia headless | PASS | XAML khởi tạo, 3 tab, status binding cập nhật; không tương đương kiểm thử tương tác trên desktop thật |
| Publish linux-x64 self-contained | PASS | Host single-file/native SQLite, Desktop multi-file/native dependencies |
| Artifact Linux self-contained chạy thực tế | PASS | DOTNET_ROOT trỏ vị trí không có runtime; C-ECHO 0000, C-STORE 0000 hai lượt, một file/row, SHA-256/IPC đúng, SIGTERM exit 0/socket được xóa |
| Publish win-x64 self-contained | PASS | Cross-publish từ Linux, xác nhận cả hai executable PE32+ x64 |
| Native libSkiaSharp trên Linux hiện tại | PASS | `ldd` resolve các dependency; không chứng minh mọi distro target đều có đủ native libraries |
| Bash/PowerShell syntax | PASS | `bash -n` và PowerShell AST parser; chưa cài service thật |
| NuGet advisory | PASS tại thời điểm kiểm tra | `dotnet list ... package --vulnerable --include-transitive` không còn advisory ở cả 8 project sau pin SQLitePCLRaw 2.1.14 và Tmds.DBus.Protocol 0.21.3 |
| Windows runtime/service/ACL/firewall | NOT RUN | Không có máy Windows trong phiên này |
| Linux cài systemd, reboot và firewall thật | NOT RUN | Không thay đổi OS services/firewall của máy development |
| Desktop thao tác thủ công trên Windows/Linux | NOT RUN | Chỉ build/publish + headless XAML/binding |
| Máy X-quang thật / máy LAN khác | NOT RUN | Networking tests dùng loopback và dữ liệu tổng hợp |
| DICOM viewer độc lập | NOT RUN | File chỉ được đọc lại/so Pixel Data bằng fo-dicom |
| Installer EXE và .deb | BUILT | NSIS Setup.exe và deb amd64 0.1.0; chi tiết tại PACKAGES.md. Chưa ký số/chưa test cài/gỡ trên OS mục tiêu; MSI chưa tạo |
| GitHub Actions matrix | Chỉ tạo workflow | Chưa push/chạy CI trên GitHub |

Báo cáo TRX tại `tests/DicomAgent.UnitTests/TestResults/verification.trx` và `tests/DicomAgent.IntegrationTests/TestResults/verification.trx` (build artifacts bị gitignore).

Hai advisory phát hiện lúc kiểm tra dependency ban đầu đã được xử lý bằng dependency pin: [D-Bus advisory](https://github.com/advisories/GHSA-xrw6-gwf8-vvr9), [SQLite advisory](https://github.com/advisories/GHSA-2m69-gcr7-jv3q). Không có advisory hiện tại không đồng nghĩa đã security-audit đầy đủ.

## Lệnh tái hiện

Từ root repository:

```bash
dotnet restore DicomAgent.sln
dotnet build DicomAgent.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test DicomAgent.sln -c Release --no-build --logger trx
bash deploy/linux/publish.sh
python3 tools/smoke-published-linux.py
pwsh -NoProfile -File deploy/windows/publish.ps1
dotnet list DicomAgent.sln package --vulnerable --include-transitive
```

Artifacts local: `publish/linux/{host,desktop}` và `publish/windows/{host,desktop}`. Source/publish scripts có thể tạo lại, không cần commit binaries.

## Chức năng hoàn thành

Receiver C-ECHO/C-STORE; negotiation SOP/Transfer Syntax explicit; validation AE/IP/UID/size; atomic final filename; SHA-256/canonical duplicate comparison; chống path traversal/symlink; metadata nullable; SQLite migration/unique index; giữ file khi DB lỗi và reconciliation; Worker service lifecycle; persistent settings; IPC quyền OS; Avalonia dashboard/settings/history; SCU/generator; deployment scripts; README/packaging/troubleshooting và CI matrix.

## Các giới hạn còn lại

- Cần kiểm thử hiện trường, Windows service/IPC ACL, Linux systemd/reboot, UI native/clipboard/folder picker, DICOM viewer độc lập và modality thật trước sử dụng lâm sàng.
- Chỉ hai Transfer Syntax uncompressed Little Endian và danh sách Storage SOP trong `StorageScp.cs`; chưa TLS, compressed codecs, Storage Commitment, Query/Retrieve, DICOMweb.
- Serialize Part 10 bảo toàn dataset/Pixel Data theo test; không byte-for-byte identity của file đầu vào.
- Mỗi storage root phải do một Host sở hữu. Commit/reconcile được serialize; dò duplicate và enumerate reconciliation còn phụ thuộc số file, cần catalog tối ưu khi dữ liệu rất lớn.
- Reconciliation chỉ quét root hiện hành, bỏ qua file đã index; không kiểm tra định kỳ file bị thay đổi/xóa ngoài Agent. Một file hỏng chưa index có thể làm vòng reconciliation dừng và báo lỗi; cần quản trị kiểm tra/phục hồi file rồi chạy tiếp.
- Lỗi schema/DB vật lý cần khôi phục SQLite; migration không tự sửa table bị xóa bằng tay. Các file hoàn chỉnh không bị mất vì lỗi này.
- Cấu hình áp dụng sau restart service; không auto restart qua UI. Environment/CLI override vẫn ưu tiên hơn settings lưu.
- Dashboard recent errors chỉ trong RAM phiên hiện tại; history UI giới hạn 100 record. Khi history tắt hoặc DB không truy cập được, dùng thống kê file thay thế nếu query DB thất bại.
- Shutdown chờ tối đa khoảng 15 giây cho associations; vượt hạn có thể ngắt truyền và thiết bị cần retry. `.partial` sau crash giữ để phục hồi thủ công. Không cam kết chống mất điện/hỏng ổ đĩa.
- DIMSE request timeout 30 giây không phải idle timeout tuyệt đối cho mọi association; đặt Receiver trên mạng được kiểm soát, giới hạn thiết bị qua firewall.
- Không tự xóa ảnh, không mã hóa ổ đĩa/SQLite sẵn; BitLocker/LUKS và backup/retention là cấu hình vận hành của phòng khám.
- Đã bổ sung NSIS Setup.exe và deb amd64; chưa tạo MSI, chưa ký số, chưa kiểm thử cài/gỡ package trên OS mục tiêu. Xem PACKAGES.md.

Danh sách file source/config/deploy/test đầy đủ: [FILES.md](FILES.md). Hướng dẫn chạy, cấu hình thiết bị và test từ máy khác: [README](../README.md).


## Kiểm chứng luồng localhost 0.2.0 — 20/11/2026

- `dotnet test DicomAgent.sln -c Release`: 24 unit + 27 integration, tổng 51 pass. Build kèm theo không có warning/error.
- Endpoint loopback thật: tạo/lấy ca, CORS preflight, chặn Origin/Host sai, Authorization, input UUID và PartnerId số/string.
- Tenant request đầu tiên được giữ sau restart/xóa hết ca; khác tenant và cùng key khác payload bị từ chối; token không xuất hiện plaintext trong request JSON hay response.
- C-STORE thật và IPC import bytes → đúng request → xem trước raw DICOM → multipart tới SaaS giả lập; kiểm tra Authorization/TenantName/ResId/ResModel/Attachments.Tenant/Attachments.Files, chặn Done lặp và ảnh đến muộn.
- Chặn chuyển ca active; association/phiên cũ không gán sang ca mới hoặc phiên nhận mới; chặn upload khi danh sách ảnh thay đổi sau xác nhận.
- HTTP 401/503/302 và restart lúc upload: giữ file, trạng thái new/error, không đi theo redirect, có thao tác đối chiếu thủ công.
- IPC tiếp tục đọc danh sách khi client khác đang import dở. Regression settings/history/DB failure/tray/Host restart vẫn pass.
- Avalonia headless + Skia render ba màn hình vào `publish/screenshots/screen-{1,2,3}.png`, đã xem và kiểm tra bố cục. Đây là dữ liệu tổng hợp, không phải ảnh bệnh nhân thật.
- Chưa chạy cài/gỡ Windows hoặc kiểm tra quyền Local Network Access từ web HTTPS thực tế; chưa gọi API SaaS bằng tài khoản thật. API SaaS được mô phỏng theo DTO IrAttachments trong source TDental.

Đóng gói 0.2.0: Windows Setup và Debian amd64 build thành công. 7-Zip kiểm tra Setup không lỗi; 417 file payload Windows khớp SHA-256 với publish. Toàn bộ md5sums payload Debian khớp sau giải nén. Binary Host Linux self-contained chạy health → nhận request có Authorization/PartnerId số → lưu tenant → SIGTERM với exit code 0. Shell/PowerShell syntax và git diff --check pass. SHA-256 artifact nằm trong publish/installers/SHA256SUMS.


## Tách lớp 0.2.1 — 20/11/2026

- Host có `CaptureRequestsController`/`HealthController` và `Api/LocalApiServer`; endpoint đi qua MVC Controller, không còn MapPost/MapGet trong Infrastructure.
- `Application/Services/CaptureRequestService` quản lý tenant, idempotency, active session, gán ảnh, xác nhận ảnh, trạng thái và điều phối upload. Phụ thuộc vào các interface Application; không phụ thuộc Infrastructure, fo-dicom hoặc ASP.NET Core.
- Infrastructure triển khai JSON repository, secret store, SaaS attachment client, DICOM preview reader và upload queue. DI đăng ký Application service tại Host, các adapter tại Infrastructure.
- `Host/Workers/CaptureUploadWorker` sở hữu vòng đời worker; Desktop IPC và DICOM adapter cùng gọi `ICaptureRequestService`.
- `dotnet test DicomAgent.sln -c Release`: 30 unit + 28 integration = 58 pass, không warning/error. Unit test project chỉ tham chiếu Application. Các ca mới kiểm tra token riêng từng request sau reload và upload đảo thứ tự, lỗi lưu không xuất hiện request chưa ghi, phân loại kết quả upload và chặn upload lại ca đã done.
- HTTP thực qua Controller vẫn pass CORS/header/body/idempotency/tenant; C-STORE, import, preview, multipart, token per request sau restart, lỗi upload và Host lifecycle vẫn pass.
- JSON schema, tenant-binding và định dạng token mã hóa giữ tương thích 0.2.0; không có migration dữ liệu mới.

Đóng gói 0.2.1: Windows Setup và Debian amd64 build thành công, payload khớp checksum với publish. Smoke test nâng cấp chạy Host 0.2.0 từ gói Debian cũ để tạo JSON/token thực, sau đó chạy Host 0.2.1 trên cùng data: Controller API đọc lại được request/token, giữ tenant, nhận thêm request với token khác mà không thay token ca cũ; SIGTERM exit code 0. Cài/gỡ Windows thực tế và API SaaS bằng tài khoản thật chưa được kiểm thử.


## Cài dùng ngay và autostart UI 0.2.2 — 20/11/2026

- Chẩn đoán máy cài thực tế: Host Running, HTTP health ready; tài khoản đã thuộc operator trong NSS nhưng session thiếu supplemental group mới, connect Unix socket bị EACCES.
- Linux postinst nhận người dùng gọi sudo/polkit, cấp named-user ACL ngay và lưu operator-uids; Host áp rw ACL khi tạo lại socket. Giữ quyền other=none và request directory mode0700. Thêm dependency acl và helper vào gói Debian.
- Windows installer cấp direct user SID trên data/named pipe; service Automatic, Start và đợi Running. Không đòi logout để làm mới group token.
- Checkbox Desktop autostart theo từng tài khoản: Windows HKCU Run, Linux XDG autostart; tắt UI startup không dừng Host. UI tự nạp lại trạng thái khi kết nối Host phục hồi.
- `dotnet test DicomAgent.sln -c Release`: 30 unit + 30 integration = 60 pass. Hai ca mới kiểm tra ACL helper/idempotency/khôi phục socket/quyền request riêng tư và enable/disable Linux UI startup không cần service. Host process test dùng port riêng để không trùng service đang cài thực tế.
- Shell/PowerShell syntax và desktop-file-validate pass; đã render/xem màn hình Cấu hình với tùy chọn mới bằng Avalonia headless.
- Chưa chạy apt/root maintainer scripts thật từ phiên agent (sudo bị giới hạn no-new-privileges); chưa cài/gỡ Windows thật hoặc kiểm tra HKCU login trên Windows. ACL đã kiểm thử trên filesystem thật; cần người dùng chạy gói mới bằng sudo apt install để áp dụng trên máy đang dùng.


### Chốt lại transport 0.2.2: HTTP localhost, không IPC

Theo yêu cầu mới, đã xóa ManagementServer/ManagementWire/UnixOperatorPermissions và dependency System.IO.Pipes.AccessControl. Web và Desktop dùng cùng HTTP loopback port. Desktop đi qua ManagementController → ManagementService → các port Application; đọc kết nối từ desktop-connection.json có quyền operator readonly. Quản trị chặn browser Origin và thiếu khóa trả401; web capture vẫn dùng Authorization/TenantName, token upload vẫn theo request.

`dotnet test DicomAgent.sln -c Release`: 30 unit + 31 integration = 61 pass, build không warning/error. Có test private HTTP auth/CORS, HTTP import file >64KiB, đọc danh sách song song khi import chưa xong, HTTP reconnect/restart và dữ liệu request/token riêng tư. Kiểm thử ACL được chuyển sang file kết nối HTTP; không tạo socket quản trị.

Đóng gói cuối 0.2.2 (HTTP-only): Windows payload và Debian md5sums khớp publish; helper/acl dependency/postinst nhận SUDO_USER có trong deb. apt-get -s xác nhận nâng 0.2.1→0.2.2 không thiếu dependency trên máy hiện tại. Binary Linux self-contained pass web capture HTTP + desktop management HTTP + chặn Origin quản trị + không tạo Unix socket + SIGTERM. Logic postinst chọn tài khoản sudo/cấp ACL/enable-start service đã chạy mô phỏng với các thao tác root được thay bằng shim, ACL thực chạy trên thư mục tạm. Chưa thực thi cài root thật hoặc Windows runtime từ phiên agent.


## Chặn trùng ca new 0.2.3 — 20/11/2026

- `CaptureRequestService.ReceiveAsync` chặn UUID mới khi đã có request `new` cùng ResModel/ResId/PartnerId; trả CaptureConflictException → HTTP409. Chỉ trạng thái new tham gia điều kiện; ca done vẫn cho phép tạo ca mới có UUID/token riêng.
- Kiểm tra và lưu JSON dùng chung semaphore; hai HTTP request đồng thời chỉ tạo một ca new. Rule có hiệu lực sau reload, cả khi không gửi Idempotency-Key và khi các trường hiển thị khác nhau.
- Gửi lại cùng Idempotency-Key và cùng body vẫn gia hạn token cho ca cũ, giữ ảnh/trạng thái. Xóa ca new cho phép tạo lại; ca done và token cũ được giữ nguyên.
- `dotnet test DicomAgent.sln -c Release`: 39 unit + 33 integration = 72 pass, không warning/error. Có test HTTP done→new→409, concurrency, numeric/string PartnerId, các trường của bộ ba thay đổi, reload/delete và token idempotency.

Đóng gói 0.2.3: Setup Windows và deb amd64 build thành công, payload/checksum khớp publish. Binary Host Linux self-contained đã chạy kiểm tra HTTP thật: new→409, seed trạng thái done khi Host dừng rồi restart→201 ca new→409; UUID và token ca cũ/mới tách biệt. Windows chưa cài thực tế; không cần migration JSON.


## Tổ chức lại code và UI 0.2.4 — 20/11/2026

- Refactor giữ nguyên logic nghiệp vụ: `CaptureRequestService` tách bước state machine rõ (receive/recover/queue/upload/resolve), `LocalApiServer` tách guard pipeline ra method riêng, Desktop ViewModel tách trạng thái capture ra `MainViewModel.Captures.cs`. Không đổi API contract, 409, idempotency, token theo request, tenant binding, phân loại upload hay storage.
- Giao diện Desktop làm mới (3 tab giữ nguyên bindings/handler): palette xanh đậm, card/pill theo trạng thái qua `StatusBrushConverter`, empty state rõ, placeholder khi chưa chọn request, nútWrapPanel xuống dòng được ở cửa sổ nhỏ. Render kiểm tra qua Avalonia headless, 3 tab chụp PNG ở `/tmp/da-screens/`.
- Bổ sung 4 unit test Application: phục hồi restart upload→review, chặn ảnh dùng chung 2 ca + attach lặp bỏ qua, 8 luồng receive đồng thời chỉ tạo 1 ca, ca done chặn bỏ từng ảnh nhưng xóa được cả ca.
- Review adversarial nhiều chiều trên diff tìm thấy 1 regression refactor thật: `SequenceEqual` so mảng `Files` theo reference khiến poll 2 giây luôn coi là "thay đổi", list rebuild liên tục (scroll reset). Đã khôi phục so sánh JSON-serialize như HEAD; build/test lại xanh.
- `dotnet test DicomAgent.sln -c Release`: 43 unit + 33 integration = 76 pass, không warning/error.
- Đóng gói 0.2.4: `publish/installers/dicom-agent_0.2.4_amd64.deb` và `DicomAgent-0.2.4-win-x64-Setup.exe` build từ source cuối (payload timestamp khớp lượt publish sau fix), SHA256SUMS cập nhật và verify toàn bộ pass. makensis chạy từ bản extract gói `nsis`+`nsis-common` 3.10 vào `/tmp/nsis-root` (máy thiếu sudo; `MAKENSIS`/`NSISDIR` override, không cài hệ thống).
- Chưa kiểm chứng: cài/gỡ Windows thật, cài root thật trên Linux, nâng cấp 0.2.3→0.2.4 trên máy có dữ liệu thật (logic giữ nguyên nên không cần migration; vẫn nên thử trên máy thử trước khi ra máy chụp chính).


## Redesign giao diện light theme + sidebar 0.2.5 — 20/11/2026

- Redesign thật của cả ba màn hình theo yêu cầu: App chuyển sang FluentTheme Light (App.axaml), token màu sáng (#F4F8FC nền, #FFFFFF card, #EAF3FF tint, #2F6FC2 nhấn, chữ #243B53, viền #DCE7F3), bo góc 8–12px, shadow tiết chế, font Inter hỗ trợ tiếng Việt, nhịp spacing 8px.
- Tab ngang thay bằng sidebar trái (RadioButton GroupName Nav + TabVisibilityConverter hai chiều với SelectedTab): logo răng vector nét mảnh, mục đang chọn nền tint + chữ nhấn, ActiveCapture ở đáy sidebar. Header riêng cho từng trang: tiêu đề + mô tả, dot trạng thái kết nối (StatusDotConverter: Running xanh, Stopped xám, Error đỏ), badge tenant, nút Làm mới gọn.
- Quản lý yêu cầu: toolbar tìm kiếm + lọc, hàng tiêu đề cột thẳng hàng với dòng dữ liệu, badge trạng thái mềm (nền tint + chữ màu qua StatusPillBackgroundConverter), thời gian gọn dd/MM/yyyy HH:mm (UTC ghi ở tiêu đề cột), Thực hiện nổi bật, Xóa thứ cấp đỏ nhạt (vẫn có hộp xác nhận như cũ), empty state có icon + hướng dẫn.
- Thực hiện dịch vụ: card thông tin ca ưu tiên bệnh nhân, ResId/ResModel/PartnerId xuống dòng phụ caption, toolbar nhóm nút, danh sách ảnh + preview lớn nền well trung tính, footer giữ nguyên checkbox đối chiếu + Done và panel review hai lựa chọn.
- Cấu hình: nhóm thành 5 card (trạng thái, máy chụp DICOM với helper phân biệt IP máy cài app / IP máy chụp, lưu trữ, web/SaaS với Expander gộp lỗi gần đây và origin bổ sung, giao diện Desktop), nút Kiểm tra/Lưu nhất quán, cảnh báo restart giữ nguyên.
- Không đổi logic: mọi bindings, command, handler, API call, trạng thái và luồng confirm giữ nguyên; chỉ App.axaml RequestedThemeVariant Dark→Light và MainWindow.axaml viết lại + 5 converter nhỏ mới (TabVisibility, StatusDot, StatusPillBackground, MessageVisibility, StatusBrush đổi màu cho tông sáng). DesktopTests cập nhật assert nav từ TabControl sang 3 RadioButton sidebar (không phải che regression — cấu trúc nav đổi theo thiết kế được yêu cầu; vẫn kiểm tra 3 mục, trạng thái chọn và chụp 3 tab).
- Render kiểm tra headless: trước `/tmp/da-before/`, sau `/tmp/da-after/` (3 tab mỗi đợt). Tự xem và sửa 4 vòng: StrokeLineJoin/StrokeLineCap không tồn tại trên Shape, StringFormat đầu {0} cần escape {}, theme variant Dark làm control sáng lẫn tối, nút mặc định trắng chữ trắng → ép style Button tông sáng.
- `dotnet test DicomAgent.sln -c Release`: 43 unit + 33 integration = 76 pass.
- Đóng gói 0.2.5: deb + Setup.exe build từ source cuối (payload 15:06 khớp publish), SHA256SUMS verify toàn bộ pass. makensis vẫn chạy từ /tmp/nsis-root.
- Chưa kiểm chứng: chạy UI thật có chuột/bàn phím trên Windows/Linux desktop, hover/selected thực tế, cửa sổ ở đúng MinWidth 980 (bố cục dùng WrapPanel + ScrollViewer nên dự kiến không tràn), cài/gỡ thật cả hai OS.

Trạng thái hover bổ sung 0.2.5 (cùng ngày): người dùng phản hồi hover cũ trắng mờ khó nhìn. Thêm :pointerover/:pressed cho Button: nút thường → nền xanh nhạt #D8E8F9 (pressed #C6DDF4), nút primary → xanh đậm #265D9F (pressed #1F4E86), nút Xóa → đỏ nhạt #F8E9EB. DesktopTests mô phỏng hover qua MouseMove headless và chụp screen-hover.png xác nhận primary đậm hơn rõ khi hover. Publish và build lại cả hai installer 0.2.5 từ source mới (payload 15:20), SHA256SUMS thay hash 0.2.5 mới, verify pass.


## Chữ trắng trên nút hover/pressed 0.2.6 — 20/11/2026

- Tái hiện regression bằng mouse event headless: primary hover có nền #265D9F nhưng caption thực tế là Black dù trạng thái thường là White. Test mới fail trước khi sửa.
- Đặt Foreground #FFFFFF trực tiếp trên ContentPresenter ở hai trạng thái primary:pointerover và primary:pressed để ghi đè foreground tương tác của Fluent theme. Màu nền giữ nguyên #265D9F / #1F4E86; không đổi nghiệp vụ hoặc layout.
- DesktopTests luôn kiểm tra caption White ở normal/hover/pressed và nền đúng ở hover/pressed, độc lập biến chụp ảnh. MouseUp ngoài nút để không thực hiện hành động nghiệp vụ. Test dùng data directory tạm để không kết nối Host đang cài hoặc đọc request/bệnh nhân thật.
- Baseline trước sửa: 43 unit + 33 integration pass. Sau sửa: 43 unit + 33 integration = 76 pass, build không warning/error. Đã xem ảnh hover/pressed synthetic tại /tmp/dicom-hover-fixed-0.2.6.
- Tăng version lên 0.2.6 để tránh apt giữ payload 0.2.5 cũ khi nhiều bộ cài trùng version. Source thay đổi UI và version; các thay đổi có sẵn trong working tree được giữ nguyên.

Đóng gói 0.2.6: cả Setup và deb build từ source cuối, Windows payload và Debian md5sums khớp publish, DLL Desktop trong deb đúng bản mới. SHA256SUMS cập nhật. apt-get -s nhận nâng 0.2.5→0.2.6 thay vì bỏ qua cùng version. Chưa cài/gỡ Windows thực tế và chưa áp dụng gói bằng sudo trên máy người dùng; cần thoát UI cũ, cài 0.2.6 rồi mở lại.

## Theme sáng/tối song song 0.2.7 — 20/11/2026

- Thêm hai palette Light/Dark trong `Themes/Colors.axaml`, dùng DynamicResource trên cả ba trang, sidebar, card, trường nhập, ảnh preview, cảnh báo và các trạng thái tương tác. FluentTheme theo lựa chọn ứng dụng; cửa sổ xác nhận kế thừa theme.
- Cấu hình kết nối → Giao diện Desktop → Chủ đề màu: Sáng/Tối, áp dụng ngay. Lưu riêng theo tài khoản tại appearance.json trong LocalApplicationData (Windows) hoặc XDG_CONFIG_HOME/~/.config (Linux), nạp lại khi mở app. Mặc định Sáng; file thiếu/hỏng trở về Sáng. Không gọi Host hay cần restart dịch vụ để đổi theme.
- Badge trạng thái nhận ActualThemeVariant qua MultiBinding để tự đổi màu trên request đang hiển thị. Tách màu accent chữ/icon và màu nền nút để theme tối có accent sáng mà nút chính vẫn có chữ trắng đủ rõ. Foreground trên ContentPresenter của nút thường/primary/danger được đặt rõ cho hover và pressed.
- Restore → Release build → test toàn solution: 43 unit + 34 integration = 77 pass, không warning/error. Test UI chọn ComboBox Sáng→Tối→Sáng, kiểm tra màu nền cửa sổ, badge, caption/nền primary normal/hover/pressed, caption/nền nút thường khi hover, đồng thời kiểm tra sidebar/tray cũ. Test lưu lựa chọn qua instance mới, JSON hỏng và ghi lại không để file tạm. Toàn bộ test theme dùng thư mục cấu hình/data tạm.
- Đã render và xem ảnh ba trang cùng phần cấu hình Chủ đề màu ở cả hai theme tại `/tmp/dicom-themes-0.2.7/`. Kiểm tra hover/pressed vẫn giữ chữ trắng trên xanh đậm. Đây là kiểm tra Avalonia headless trên Linux; chưa thao tác/cài thực tế trên Windows hoặc cài root Linux.
- Giữ nguyên API, token upload theo request, tenant, logic nhận/gán DICOM, chặn ca new trùng, luồng Done/review và định dạng dữ liệu Host. Các thay đổi có sẵn trong working tree được giữ lại.

Đóng gói 0.2.7: Windows Setup và Debian amd64 được build từ source cuối. Trích xuất đối chiếu 417 file Windows và 217 file Linux với publish: khớp toàn bộ; 225 md5sums của deb và SHA256SUMS các installer đều pass. apt-get -s nhận nâng 0.2.6→0.2.7, không thêm/xóa package khác. Chưa thực thi cài/gỡ thật trong phiên agent.

## Lưu cấu hình và tự áp dụng 0.2.8 — 20/11/2026

- Tái hiện lỗi trên service đang cài: lưu nguyên cấu hình (storage trống) thành công; chọn `/home/duynguyen/DicomImg/` trả lỗi chung, settings cũ không thay đổi. Nguyên nhân: service chạy bằng `dicom-agent`, home có quyền hạn chế và unit bật `ProtectHome=true`.
- Theo yêu cầu mới, nút Lưu Desktop không yêu cầu chạy lệnh thủ công: tạo tệp cấu hình tạm riêng, gọi Host đã cài bằng polkit (Linux) hoặc UAC (Windows). Helper chỉ chạy khi có quyền quản trị, không lấy data path từ môi trường caller, chỉ nhận input tạm của app, dùng lease chống hai lượt áp dụng song song và không chứa token SaaS.
- Linux: grant ACL cho đúng thư mục ảnh và quyền traverse cho service; grant đọc theo PKEXEC_UID cho operator. Với thư mục home, dùng ProtectHome=tmpfs + BindPaths/ReadWritePaths đúng thư mục, không mở toàn bộ home. Thêm pkexec dependency và policy chỉ khớp Host --apply-settings. Windows: grant Modify cho NT SERVICE\DicomAgent trên đúng thư mục bằng icacls; thao tác service qua PowerShell hệ thống, không hiện console.
- Helper chặn áp dụng khi đang upload SaaS; chuẩn bị ACL/sandbox, dừng service để drain association, lưu JSON cùng schema, sửa owner settings Linux, start service, rồi gọi management storage-check để chính service kiểm tra ghi trong sandbox thực. Fail ở bước ghi/start/probe: dừng service mới, phục hồi settings và sandbox/ACL trước đó, start lại service cũ. Không di chuyển/xóa ảnh, request, tenant hay token.
- Desktop tạm khóa cấu hình/thao tác capture và dừng polling trong lúc áp dụng, tự kết nối lại sau khi service sẵn sàng. Cần bật nhận lại cho ca đang thực hiện sau restart, như semantics restart hiện có. Theme/autostart theo user không cần elevation.
- Endpoint web và API management save cũ giữ nguyên. Thêm operation storage-check chỉ trong management có local key, không ghi settings hay đặt RestartRequired. Receiver vẫn dùng snapshot từng tiến trình, không hot-mutate options giữa association.
- Kiểm chứng giới hạn: không thể chạy quyền root/polkit thật từ phiên agent có NoNewPrivs, chưa cài/chạy UAC thật trên Windows. Các thao tác ACL/service của helper được kiểm thử bằng runner giả lập; quyền write probe, HTTP management và Host process thường được kiểm thử thật trên data tạm. Policy XML và systemd unit/drop-in mẫu đã validate. Cần thử thao tác xác thực thực tế sau khi cài bộ cài mới.

Kết quả 0.2.8: restore → build Release → test toàn solution tuần tự; 43 unit + 43 integration = 86 pass, không warning/error. Chín test mới kiểm tra thứ tự prepare/stop/write/start, ACL operator, rollback khi start lỗi, rollback sau probe của service mới lỗi (service cũ phải thực sự được stop/start lại), từ chối storage/input không phù hợp và symlink input, Host command từ chối tài khoản không nâng quyền, storage-check không đổi settings/restart flag, và save lỗi vẫn giữ config/Receiver C-ECHO hoạt động. Đã render/xem UI dark và test màu light/dark/hover/pressed như bản trước tại /tmp/dicom-settings-0.2.8.

Đóng gói 0.2.8 hoàn tất: 417 file Windows và 217 file Linux trích xuất từ bộ cài khớp publish cuối; 226 checksum Debian pass, pkexec dependency và policy polkit có trong deb. SHA256SUMS của toàn bộ installer đã xác minh. Linux Host self-contained nhận đúng --apply-settings và từ chối chạy bằng user chưa nâng quyền, không khởi động Host thường. apt-get -s nhận nâng 0.2.7→0.2.8, không thêm/xóa package khác trên máy hiện tại. Bộ cài chưa được áp dụng lên máy từ phiên agent.

## Một lệnh release Windows + Linux — 20/11/2026

- Thêm deploy/release.sh: preflight toolchain/SDK, tìm NSIS ở PATH/override/cache user cố định, khóa chống hai lượt build đồng thời, tự tăng patch và đồng bộ version project/script/tài liệu hiện hành. Restore → build → test tuần tự; clean đúng thư mục binary publish, publish mới từng RID rồi tạo cả Setup.exe và deb. Trích xuất đối chiếu từng file với publish, xác minh Debian md5sums và ghi SHA256SUMS sau khi thành công.
- NSIS của máy build đã chuẩn bị tại ~/.cache/dicom-agent-build/nsis (không phụ thuộc /tmp). Preflight đã thử: version cũ, compiler thiếu và release khác đang giữ lock đều dừng trước khi sửa version. bash -n và --help pass. Không tự cài app, chạy lệnh sudo, restart service, commit hoặc push.
- Lượt 0.2.9 dừng trước publish vì hai integration test upload fail. Xác minh source upload đã bỏ header TenantName/Idempotency-Key; người dùng xác nhận giữ thay đổi đó và cập nhật test theo hợp đồng mới. Test upload kiểm tra hai header không tồn tại, tenant multipart vẫn đúng, Authorization vẫn riêng cho từng request sau reload; tương quan request bằng ResId trong body. Hợp đồng web gọi localhost vẫn giữ hai header như trước.
- Chạy lại chính lệnh bash deploy/release.sh tự tăng sang 0.2.10: restore/build sạch, 43 unit + 43 integration = 86 pass. Không bỏ qua test lỗi hoặc tự retry upload.

Release một lệnh 0.2.10 hoàn tất thành công (exit 0): tạo cả Windows Setup.exe và Linux deb từ publish sạch; trích xuất so payload và Debian md5sums pass; SHA256SUMS cập nhật. Lệnh chạy trực tiếp không cần MAKENSIS/NSISDIR từ /tmp, dùng cache NSIS cố định. Lần release tiếp theo mặc định tăng 0.2.10→0.2.11.

## Hướng dẫn lệnh thao tác 0.2.11 — 20/11/2026

Thêm docs/LENH-RELEASE-VA-GIA-LAP.md: lệnh release tự tăng patch, nơi lấy bộ cài, cấu hình loopback và chọn request trước khi nhận, lệnh echo/generate/store gửi một ảnh thử, kết quả mong đợi và lệnh build lại simulator Linux khi cần. Kiểm tra đường dẫn script/simulator tồn tại và Markdown đóng đủ code fence. Chạy release.sh hoàn tất 0.2.11: 43 unit + 43 integration pass, cả Setup/deb đã tạo và xác minh payload/checksum. Không sửa logic ứng dụng trong lượt thêm tài liệu này.

## Tự chọn SDK đúng khi release từ PowerShell 0.2.12 — 20/11/2026

- Lỗi tái hiện theo môi trường người dùng: PATH PowerShell chỉ có /usr/bin:/bin, DOTNET_ROOT=/usr/lib/dotnet, hệ thống có SDK 8.0.134 trong khi global.json yêu cầu 8.0.422 (latestPatch). Máy đã có SDK phù hợp ở ~/.dotnet.
- Thêm deploy/use-dotnet.sh và gọi trong preflight release, trước khi đổi version. Thử từng host từ PATH, DOTNET_ROOT, ~/.dotnet và vị trí hệ thống; để chính dotnet resolve global.json/rollForward. Chọn được host thì export PATH, DOTNET_ROOT, DOTNET_ROOT_X64 và DOTNET_HOST_PATH cho toàn bộ các bước con. Không đổi global.json, không hạ SDK.
- Kiểm tra thật từ pwsh với PATH/DOTNET_ROOT hệ thống cũ: Bash và PowerShell con đều tìm đúng /home/duynguyen/.dotnet/dotnet, SDK 8.0.422. Repo tạm pin SDK không tồn tại 99.0.100: release dừng ở preflight, Directory.Build.props không thay đổi. bash -n và git diff --check pass.
- Chạy release đầy đủ từ chính PowerShell với môi trường lỗi nêu trên: tự tăng 0.2.11→0.2.12, restore/build sạch, 43 unit + 43 integration = 86 pass. Không sửa code ứng dụng hay hợp đồng upload trong lượt sửa SDK này.

Release 0.2.12 từ PowerShell với PATH hệ thống cũ hoàn tất exit 0: cả Windows Setup và Linux deb tạo thành công, trích xuất đối chiếu publish/md5sums pass, SHA256SUMS cập nhật. Lệnh người dùng vẫn là bash deploy/release.sh; không cần export PATH thủ công.
